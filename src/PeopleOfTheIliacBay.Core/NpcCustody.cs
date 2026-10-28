using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcCustodyPhase {Escorting,Confined,Returning,Released,Cancelled}
    [Serializable] public sealed class NpcCustodyRecord
    {
        public string PersonId,GuardId,CaseId,CellDoorId;
        public int SettlementId,NativeCrime,PrisonDays,FineGold;
        public long Revision;
        public NpcCustodyPhase Phase;
        public double UpdatedAt,ConfinedAt,ReleaseAt;
        public NpcActionTarget Cell,ReleasePoint,LastPosition;
        public NpcCustodyPortal Portal;
    }

    public sealed partial class WorldStateStore
    {
        public NpcCustodyRecord ReadNpcCustody(string personId)
        {var value=state.NpcCustodies.Find(row=>row.PersonId==personId);return value==null?null:CopyNpcCustody(value);}
        public bool HasActiveNpcCustody(string personId)
        {var row=state.NpcCustodies.Find(value=>value.PersonId==personId);return row!=null && row.Phase<NpcCustodyPhase.Released;}
        public bool HasNpcCustodyEscort(string guardId)
        {return state.NpcCustodies.Exists(row=>row.GuardId==guardId && HoldsCustodyGuard(row));}

        // A loaded native adapter supplies real free-cell/path evidence. Merely
        // naming a door, a dungeon or a floor point cannot call this successfully.
        public bool TryBeginNpcCustody(NpcCustodyRecord proposal,bool actualCellAvailable)
        {
            if(proposal==null || !actualCellAvailable || HasActiveNpcCustody(proposal.PersonId) ||
                state.NpcCustodies.Count>=256 && !state.NpcCustodies.Exists(row=>row.PersonId==proposal.PersonId))return false;
            NpcCustodyRecord copy;try{copy=CopyNpcCustody(proposal);}catch(ArgumentException){return false;}
            bool alive;var roster=GetGuards(copy.SettlementId);
            var guard=roster==null?null:roster.Guards.Find(value=>value.Id==copy.GuardId);
            var report=roster==null?null:roster.Reports.Find(value=>value.Id==copy.CaseId);
            if(!KnownNpcIdentity(state,copy.PersonId,out alive) || !alive || guard==null || !guard.IsAlive ||
                guard.IncidentId!=copy.CaseId || report==null ||
                report.ThreatId!=copy.PersonId && (report.CorpseSuspect==null || report.CorpseSuspect.Identity!=copy.PersonId) ||
                copy.Phase!=NpcCustodyPhase.Escorting || copy.Revision!=0 || copy.ConfinedAt!=0 || copy.ReleaseAt!=0 ||
                state.NpcCustodies.Exists(row=>row.Phase<NpcCustodyPhase.Released &&
                    (HoldsCustodyGuard(row) && row.GuardId==copy.GuardId || row.Cell.WorldSpaceId==copy.Cell.WorldSpaceId && row.CellDoorId==copy.CellDoorId)))return false;
            state.NpcCustodies.RemoveAll(row=>row.PersonId==copy.PersonId);state.NpcCustodies.Add(copy);return true;
        }
        public bool TryObserveNpcCustody(string personId,long revision,NpcActionTarget actual,double now)
        {
            var row=state.NpcCustodies.Find(value=>value.PersonId==personId);
            if(!CurrentCustody(row,revision,now) || actual==null)return false;
            NpcActionTarget position;try{position=CustodyPoint(actual);}catch(ArgumentException){return false;}
            if(row.Phase==NpcCustodyPhase.Confined && !CustodyNear(position,row.Cell,.4))return false;
            row.LastPosition=position;row.UpdatedAt=now;row.Revision++;
            if(row.Phase==NpcCustodyPhase.Returning && CustodyNear(position,row.ReleasePoint,.4))row.Phase=NpcCustodyPhase.Released;
            return true;
        }
        public bool TryConfineNpc(string personId,long revision,double now,bool actualDoorClosed,bool actualDoorLocked,int[] nativeRandomValues)
        {
            var row=state.NpcCustodies.Find(value=>value.PersonId==personId);
            if(!CurrentCustody(row,revision,now) || row.Phase!=NpcCustodyPhase.Escorting ||
                !actualDoorClosed || !actualDoorLocked || !CustodyNear(row.LastPosition,row.Cell,.4))return false;
            var inventory=ReadNpcInventory(personId);if(inventory==null)return false;
            NpcNativeSentenceResult sentence;try{sentence=NpcNativeSentence.Calculate(row.NativeCrime,0,inventory.WalletGold,nativeRandomValues);}catch(ArgumentException){return false;}
            if(sentence.FineGold>0 && !TryPayNpcService(personId,sentence.FineGold,"native-custody-fine:"+personId+":"+row.CaseId,inventory.Revision))return false;
            row.PrisonDays=sentence.PrisonDays;row.FineGold=sentence.FineGold;row.ConfinedAt=now;
            row.ReleaseAt=now+sentence.PrisonDays*86400d;row.Phase=NpcCustodyPhase.Confined;row.UpdatedAt=now;row.Revision++;return true;
        }
        public bool TryReleaseNpcCustody(string personId,long revision,double now)
        {
            var row=state.NpcCustodies.Find(value=>value.PersonId==personId);
            if(!CurrentCustody(row,revision,now) || row.Phase!=NpcCustodyPhase.Confined || now<row.ReleaseAt)return false;
            row.Phase=NpcCustodyPhase.Returning;row.UpdatedAt=now;row.Revision++;return true;
        }
        public bool TryCancelNpcCustody(string personId,long revision,double now)
        {
            var row=state.NpcCustodies.Find(value=>value.PersonId==personId);
            if(!CurrentCustody(row,revision,now))return false;
            row.Phase=NpcCustodyPhase.Cancelled;row.UpdatedAt=now;row.Revision++;return true;
        }
        private static bool CurrentCustody(NpcCustodyRecord row,long revision,double now)
        {return row!=null && row.Phase<NpcCustodyPhase.Released && row.Revision==revision && row.Revision<long.MaxValue &&
            !double.IsNaN(now) && !double.IsInfinity(now) && now>=row.UpdatedAt;}
        private static bool CustodyNear(NpcActionTarget a,NpcActionTarget b,double radius)
        {return a.WorldSpaceId==b.WorldSpaceId && a.SettlementId==b.SettlementId && a.BuildingKey==b.BuildingKey &&
            (a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)+(a.Z-b.Z)*(a.Z-b.Z)<=radius*radius;}
        private static NpcActionTarget CustodyPoint(NpcActionTarget value)
        {
            NpcActionContract.ValidateTarget(value);
            Require((value.Kind==NpcActionTargetKind.WorldPoint || value.Kind==NpcActionTargetKind.IndoorPoint) &&
                !string.IsNullOrWhiteSpace(value.WorldSpaceId),"Custody position needs its actual loaded space.");
            return NpcActionExecutor.CopyTarget(value);
        }
        private static NpcCustodyRecord CopyNpcCustody(NpcCustodyRecord value)
        {
            ulong door;
            Require(value!=null && !string.IsNullOrWhiteSpace(value.PersonId) && value.PersonId.Length<=240 &&
                !string.IsNullOrWhiteSpace(value.GuardId) && value.GuardId.Length<=240 && !string.IsNullOrWhiteSpace(value.CaseId) && value.CaseId.Length<=240 &&
                ulong.TryParse(value.CellDoorId,NumberStyles.None,CultureInfo.InvariantCulture,out door) && door>0 &&
                value.SettlementId>=0 && value.NativeCrime>=1 && value.NativeCrime<=15 && (int)value.Phase>=0 && (int)value.Phase<=4 &&
                value.Revision>=0 && value.UpdatedAt>=0 && !double.IsNaN(value.UpdatedAt) && !double.IsInfinity(value.UpdatedAt) &&
                value.PrisonDays>=0 && value.PrisonDays<=900 && value.FineGold>=0 && value.FineGold<=12000 &&
                value.ConfinedAt>=0 && value.ReleaseAt>=value.ConfinedAt && !double.IsInfinity(value.ReleaseAt) && !double.IsNaN(value.ReleaseAt),"Invalid NPC custody record.");
            Require(value.Phase!=NpcCustodyPhase.Escorting || value.ConfinedAt==0 && value.ReleaseAt==0 && value.FineGold==0 && value.PrisonDays==0,
                "An escort cannot already have a prison sentence.");
            Require(value.Phase==NpcCustodyPhase.Escorting || value.Phase==NpcCustodyPhase.Cancelled ||
                value.ConfinedAt<=value.UpdatedAt && value.ReleaseAt==value.ConfinedAt+value.PrisonDays*86400d,
                "NPC custody sentence timing differs from native prison days.");
            var cell=CustodyPoint(value.Cell);var release=CustodyPoint(value.ReleasePoint);var last=CustodyPoint(value.LastPosition);
            Require(cell.SettlementId==value.SettlementId && release.SettlementId==value.SettlementId && last.SettlementId==value.SettlementId,
                "NPC custody points must belong to their actual settlement.");
            return new NpcCustodyRecord{PersonId=value.PersonId,GuardId=value.GuardId,CaseId=value.CaseId,CellDoorId=value.CellDoorId,
                SettlementId=value.SettlementId,NativeCrime=value.NativeCrime,PrisonDays=value.PrisonDays,FineGold=value.FineGold,
                Revision=value.Revision,Phase=value.Phase,UpdatedAt=value.UpdatedAt,ConfinedAt=value.ConfinedAt,ReleaseAt=value.ReleaseAt,
                Cell=cell,ReleasePoint=release,LastPosition=last,Portal=CopyCustodyPortal(value.Portal,cell,last,value.Phase)};
        }
        private static List<NpcCustodyRecord> CopyNpcCustodies(WorldState source,WorldState validated)
        {
            var result=new List<NpcCustodyRecord>();var ids=new HashSet<string>(StringComparer.Ordinal);
            // An optional empty list adds no migration path or version bypass.
            if(source.NpcCustodies==null)return result;
            Require(source.NpcCustodies.Count<=256,"NPC custody ledger exceeds its bound.");
            foreach(var value in source.NpcCustodies)
            {
                var copy=CopyNpcCustody(value);bool alive;
                var roster=validated.Garrisons.Find(row=>row.SettlementId==copy.SettlementId);
                Require(ids.Add(copy.PersonId) && KnownNpcIdentity(validated,copy.PersonId,out alive) && roster!=null &&
                    roster.Guards.Exists(guard=>guard.Id==copy.GuardId),"Unknown or duplicate NPC custody owner/guard.");
                Require(!result.Exists(other=>other.Phase<NpcCustodyPhase.Released && copy.Phase<NpcCustodyPhase.Released &&
                    (HoldsCustodyGuard(other) && HoldsCustodyGuard(copy) && other.GuardId==copy.GuardId ||
                     other.Cell.WorldSpaceId==copy.Cell.WorldSpaceId && other.CellDoorId==copy.CellDoorId)),"Custody guard or cell is assigned twice.");
                result.Add(copy);
            }
            return result;
        }
    }
}
