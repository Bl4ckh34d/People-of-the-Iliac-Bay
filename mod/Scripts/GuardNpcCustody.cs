using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private bool IsCustodyEscort(GuardRecord guard)
        {return worldStore!=null && worldStore.HasNpcCustodyEscort(guard.Id);}
        private bool TryStartNpcCustody(GuardRecord guard,GuardActor escort,GuardReport report,
            DaggerfallEntityBehaviour target,double now)
        {
            var cell=NativeNpcCustody.Cell;
            var resident=target?target.GetComponent<ResidentActor>():null;
            var owner=target?target.GetComponent<NativeNpcInventoryOwner>():null;
            bool murder=IsKnownMurdererDefense(guard,report,target);
            Transform exterior=null;
            bool outside=cell!=null && escort && target && NativeNpcCustody.ExteriorCellAvailable(cell,worldStore,
                escort.GroundPosition,target.transform.position-Vector3.up,out exterior);
            bool sameRoom=cell!=null && cell.Current && escort && target && escort.transform.IsChildOf(cell.Root) && target.transform.IsChildOf(cell.Root);
            if(worldStore==null || cell==null || !(outside || sameRoom) || !resident || resident.Resident==null ||
                !resident.Resident.IsAlive || resident.Resident.IsQuestProtected || !owner || owner.Binding==null ||
                !escort || outside && (!escort.transform.IsChildOf(exterior) || !target.transform.IsChildOf(exterior)) ||
                cell.Settlement!=Roster.SettlementId || report==null || report.ThreatId!=resident.Resident.Id ||
                !(murder || IsWitnessedResidentAssault(target,now)) ||
                CivilianAlarms.ChooseReaction(resident.Resident)!=CivilianReaction.Flee ||
                Vector3.Distance(escort.transform.position,target.transform.position)>1.65f || target.Entity.IsInvisible ||
                !CivilianResponses.Sees(escort.transform.position,target.transform.position,2) ||
                !outside && (!cell.Door.IsClosed || !cell.Door.IsLocked ||
                !cell.Path(escort.GroundPosition,cell.Outside) || !cell.Path(target.transform.position-Vector3.up,cell.Outside)))return false;
            foreach(var occupant in outside?new DaggerfallEntityBehaviour[0]:UnityEngine.Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
                if(occupant!=target && occupant.Entity!=null && occupant.Entity.CurrentHealth>0 &&
                    Vector3.Distance(occupant.transform.position-Vector3.up,cell.Inside)<.6f)return false;
            var position=outside?NativeNpcCustody.SpacePoint(exterior,target.transform.position-Vector3.up,cell.Settlement,"city-"+cell.Settlement):cell.Position(target.transform.position-Vector3.up);
            var portal=outside?NativeNpcCustody.CopyPortal(cell.Portal):null;
            if(portal!=null)portal.GuardPosition=NativeNpcCustody.SpacePoint(exterior,escort.GroundPosition,cell.Settlement,"city-"+cell.Settlement);
            var proposal=new NpcCustodyRecord {PersonId=resident.Resident.Id,GuardId=guard.Id,CaseId=report.Id,
                SettlementId=Roster.SettlementId,NativeCrime=(int)(murder?PlayerEntity.Crimes.Murder:PlayerEntity.Crimes.Assault),
                CellDoorId=outside?cell.PortalCellDoorId:cell.Door.LoadID.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Cell=outside?cell.SurveyedInside:cell.Position(cell.Inside),ReleasePoint=position,LastPosition=position,UpdatedAt=now,Portal=portal};
            if(!worldStore.TryBeginNpcCustody(proposal,true))return false;
            NativeNpcConfiscationResult confiscation;string reason;
            if(!NativeNpcConfiscation.TryApply(worldStore,owner.Binding,target,null,id=>NativeNpcCustody.LivingOwner(worldStore,id),
                NativeNpcCustody.ShopStock,NativeNpcCustody.WallContainer(),
                ()=>(outside?cell.KnownExterior(worldStore):cell.Current) && target && target.Entity.CurrentHealth>0 && worldStore.HasActiveNpcCustody(proposal.PersonId),
                out confiscation,out reason))
            {var failed=worldStore.ReadNpcCustody(proposal.PersonId);worldStore.TryCancelNpcCustody(proposal.PersonId,failed.Revision,now);
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NPC_CUSTODY_REFUSED: confiscation="+reason);return false;}
            if(guard.Interior==null && guard.Duty!=GuardDuty.Engaged)Roster.Engage(guard.Id,now);
            escort.EndCombat();escort.Capture(now);
            var session=resident.GetComponent<NativeNpcCustodySession>()??resident.gameObject.AddComponent<NativeNpcCustodySession>();
            session.Bind(worldStore,resident,cell,escort);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_CUSTODY_ESCORT: person="+proposal.PersonId+"; guard="+guard.Id+
                "; returned="+confiscation.Returned+"; stored="+confiscation.Stored+"; destroyed="+confiscation.Destroyed);
            return true;
        }
    }
}
