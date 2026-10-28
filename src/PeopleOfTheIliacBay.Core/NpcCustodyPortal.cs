using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcCustodyPortalStage {Exterior,Entering,Interior,Exiting,ExteriorReturn}
    // Qualified by the loaded native adapter, then retained only by this case.
    // This is not a travel action or a catalog of inferred prison locations.
    [Serializable] public sealed class NpcCustodyPortal
    {
        public string EntranceId,Profile,Variant;
        public NpcActionTarget Exterior,Entry,CellOutside,GuardPosition,CellMinimum,CellMaximum;
        public NpcCustodyPortalStage Stage;
        public bool GuardReturned;
    }

    public sealed partial class WorldStateStore
    {
        private static bool HoldsCustodyGuard(NpcCustodyRecord row)
        {return row.Phase<NpcCustodyPhase.Released && (row.Phase==NpcCustodyPhase.Escorting || row.Portal!=null && !row.Portal.GuardReturned);}
        private static NpcCustodyPortal CopyCustodyPortal(NpcCustodyPortal value,NpcActionTarget cell,NpcActionTarget last,NpcCustodyPhase phase)
        {
            if(value==null)return null;
            Require(!string.IsNullOrWhiteSpace(value.EntranceId) && value.EntranceId.Length<=240 &&
                value.Profile!=null && value.Profile.Length<=8192 && value.Variant!=null && value.Variant.Length<=1024 &&
                (int)value.Stage>=0 && (int)value.Stage<=4,"Invalid native custody portal identity.");
            var exterior=CustodyPoint(value.Exterior);var entry=CustodyPoint(value.Entry);var outside=CustodyPoint(value.CellOutside);
            var guard=CustodyPoint(value.GuardPosition);
            var minimum=CustodyPoint(value.CellMinimum);var maximum=CustodyPoint(value.CellMaximum);
            Require(cell.Kind==NpcActionTargetKind.WorldPoint && cell.WorldSpaceId=="dungeon-"+cell.SettlementId &&
                exterior.Kind==NpcActionTargetKind.WorldPoint && exterior.SettlementId==cell.SettlementId && exterior.WorldSpaceId=="city-"+cell.SettlementId &&
                CustodySameSpace(entry,cell) && CustodySameSpace(outside,cell) && CustodySameSpace(minimum,cell) && CustodySameSpace(maximum,cell) &&
                (CustodySameSpace(guard,exterior) || CustodySameSpace(guard,entry)),"Custody portal must join the actual local exterior and Dungeon.");
            bool inside=value.Stage==NpcCustodyPortalStage.Interior || value.Stage==NpcCustodyPortalStage.Exiting;
            Require(CustodySameSpace(last,inside?entry:exterior) &&
                (phase!=NpcCustodyPhase.Confined || value.Stage==NpcCustodyPortalStage.Interior) &&
                (value.Stage!=NpcCustodyPortalStage.Entering || phase==NpcCustodyPhase.Escorting || phase==NpcCustodyPhase.Cancelled) &&
                (value.Stage!=NpcCustodyPortalStage.Exiting && value.Stage!=NpcCustodyPortalStage.ExteriorReturn || phase>=NpcCustodyPhase.Returning),
                "Custody portal phase and actual position disagree.");
            Require(!value.GuardReturned || CustodySameSpace(guard,exterior),"Returned guard needs its actual exterior position.");
            Require(maximum.X>minimum.X && maximum.X-minimum.X<=16 && maximum.Z>minimum.Z && maximum.Z-minimum.Z<=16 &&
                maximum.Y>minimum.Y && maximum.Y-minimum.Y<=8 && cell.X>=minimum.X && cell.X<=maximum.X &&
                cell.Z>=minimum.Z && cell.Z<=maximum.Z && cell.Y>=minimum.Y && cell.Y<=maximum.Y,"Invalid actual custody cell bounds.");
            return new NpcCustodyPortal {EntranceId=value.EntranceId,Profile=value.Profile,Variant=value.Variant,
                Exterior=exterior,Entry=entry,CellOutside=outside,GuardPosition=guard,CellMinimum=minimum,CellMaximum=maximum,
                Stage=value.Stage,GuardReturned=value.GuardReturned};
        }
        private static bool CustodySameSpace(NpcActionTarget first,NpcActionTarget last)
        {return first!=null && last!=null && first.Kind==last.Kind && first.SettlementId==last.SettlementId &&
            first.BuildingKey==last.BuildingKey && first.WorldSpaceId==last.WorldSpaceId;}
        public bool TryBeginNpcCustodyPortal(string person,long revision,double now,bool entering,string entranceId,bool actualAtDoor)
        {
            var row=state.NpcCustodies.Find(value=>value.PersonId==person);var portal=row==null?null:row.Portal;
            if(!CurrentCustody(row,revision,now) || portal==null || !actualAtDoor || portal.EntranceId!=entranceId ||
                entering && (row.Phase!=NpcCustodyPhase.Escorting || portal.Stage!=NpcCustodyPortalStage.Exterior ||
                    !CustodyNear(row.LastPosition,portal.Exterior,1.5) || !CustodyNear(portal.GuardPosition,portal.Exterior,1.5)) ||
                !entering && (row.Phase!=NpcCustodyPhase.Returning || portal.Stage!=NpcCustodyPortalStage.Interior ||
                    !CustodyNear(row.LastPosition,portal.Entry,1.5)))return false;
            portal.Stage=entering?NpcCustodyPortalStage.Entering:NpcCustodyPortalStage.Exiting;
            row.UpdatedAt=now;row.Revision++;return true;
        }
        public bool TryCompleteNpcCustodyPortal(string person,long revision,double now,NpcActionTarget actual,bool actualNativeTransition,NpcActionTarget actualGuard=null)
        {
            var row=state.NpcCustodies.Find(value=>value.PersonId==person);var portal=row==null?null:row.Portal;
            if(!CurrentCustody(row,revision,now) || portal==null || !actualNativeTransition ||
                portal.Stage!=NpcCustodyPortalStage.Entering && portal.Stage!=NpcCustodyPortalStage.Exiting)return false;
            bool entering=portal.Stage==NpcCustodyPortalStage.Entering;
            NpcActionTarget landing,guard=null;
            try {landing=CustodyPoint(actual);if(entering)guard=CustodyPoint(actualGuard);}catch(ArgumentException){return false;}
            if(!CustodyNear(landing,entering?portal.Entry:portal.Exterior,1.5) || entering && !CustodyNear(guard,portal.Entry,1.5))return false;
            portal.Stage=entering?NpcCustodyPortalStage.Interior:NpcCustodyPortalStage.ExteriorReturn;
            row.LastPosition=landing;if(entering)portal.GuardPosition=guard;row.UpdatedAt=now;row.Revision++;return true;
        }
        public bool TryAbortNpcCustodyPortal(string person,long revision,double now)
        {
            var row=state.NpcCustodies.Find(value=>value.PersonId==person);var portal=row==null?null:row.Portal;
            if(!CurrentCustody(row,revision,now) || portal==null ||
                portal.Stage!=NpcCustodyPortalStage.Entering && portal.Stage!=NpcCustodyPortalStage.Exiting)return false;
            portal.Stage=portal.Stage==NpcCustodyPortalStage.Entering?NpcCustodyPortalStage.Exterior:NpcCustodyPortalStage.Interior;
            row.UpdatedAt=now;row.Revision++;return true;
        }
        public bool TryObserveNpcCustodyGuard(string person,long revision,NpcActionTarget actual,double now)
        {
            var row=state.NpcCustodies.Find(value=>value.PersonId==person);
            if(!CurrentCustody(row,revision,now) || row.Portal==null || row.Portal.GuardReturned)return false;
            NpcActionTarget position;try{position=CustodyPoint(actual);}catch(ArgumentException){return false;}
            if(!CustodySameSpace(position,row.Portal.Stage==NpcCustodyPortalStage.Exterior?row.Portal.Exterior:row.Portal.Entry))return false;
            row.Portal.GuardPosition=position;row.UpdatedAt=now;row.Revision++;return true;
        }
        public bool TryReturnNpcCustodyGuard(string person,long revision,double now,NpcActionTarget actual,string entranceId,bool nativeExit)
        {
            var row=state.NpcCustodies.Find(value=>value.PersonId==person);var portal=row==null?null:row.Portal;
            if(!CurrentCustody(row,revision,now) || portal==null || portal.GuardReturned || !nativeExit ||
                row.Phase!=NpcCustodyPhase.Confined || portal.Stage!=NpcCustodyPortalStage.Interior || portal.EntranceId!=entranceId ||
                !CustodyNear(portal.GuardPosition,portal.Entry,1.5))return false;
            NpcActionTarget landing;try{landing=CustodyPoint(actual);}catch(ArgumentException){return false;}
            if(!CustodyNear(landing,portal.Exterior,1.5))return false;
            portal.GuardReturned=true;portal.GuardPosition=landing;row.UpdatedAt=now;row.Revision++;return true;
        }
        public void KeepNpcCustodyGuards(double now)
        {
            if(double.IsNaN(now) || double.IsInfinity(now) || now<0)return;
            foreach(var row in state.NpcCustodies)
            {
                if(!HoldsCustodyGuard(row))continue;
                var roster=GetGuards(row.SettlementId);var guard=roster==null?null:roster.Guards.Find(value=>value.Id==row.GuardId);
                if(guard!=null && guard.IsAlive && guard.IncidentId==row.CaseId && guard.Duty==GuardDuty.Engaged)
                    guard.SearchUntil=Math.Max(guard.SearchUntil,now+300);
            }
        }
    }
}
