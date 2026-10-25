using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class HealthServiceChoice
        {
            internal NpcServiceContractWriteTicket Ticket;
            internal NpcServiceContract Contract;
            internal NpcGoalServiceHealthAdmission Admission;
            internal bool ActiveDuty, Ambiguous;
        }

        private readonly Dictionary<string,float> healthServiceSettlementRetryAt = new Dictionary<string,float>(StringComparer.Ordinal);
        private readonly HashSet<string> healthServiceSettlementFatal = new HashSet<string>(StringComparer.Ordinal);

        private HealthServiceChoice ChooseHealthService(string personId,NpcHealthCareRecord care,
            ResidentActor actor,NpcTaskPlan proposed,double now)
        {
            var choice=new HealthServiceChoice {Ticket=worldStore.CaptureNpcServiceContractWriteTicket()};
            int active=0,owned=0,allActive=0,allOwned=0;
            NpcServiceContract activeContract=null,ownedContract=null;
            foreach(var contract in choice.Ticket.SourceLedger.Contracts)
            {
                if(contract.NpcId!=personId)continue;
                if(contract.Status==NpcServiceContractStatus.Active)
                {allActive++;if(now<contract.BlockEndAt){active++;activeContract=contract;}}
                else if(contract.Status==NpcServiceContractStatus.Suspended &&
                    contract.SuspensionOwnerKind==NpcServiceSuspensionOwnerKind.HealthCare)
                {allOwned++;if(now<contract.BlockEndAt){owned++;ownedContract=contract;}}
            }
            choice.ActiveDuty=active>0;
            choice.Ambiguous=allActive>1 || allOwned>1 || allActive>0 && allOwned>0;
            if(choice.Ambiguous)return choice;
            choice.Contract=active==1?activeContract:owned==1?ownedContract:null;
            if(choice.Contract==null)return choice;
            if(active==0)choice.Admission=NpcGoalServiceHealthAdmission.RequiresPause;
            else if(IsNegotiatedPaidHealthRest(choice.Contract,actor,proposed,now))
                choice.Admission=NpcGoalServiceHealthAdmission.PaidRest;
            else choice.Admission=NpcGoalServiceHealthAdmission.RequiresPause;
            return choice;
        }

        private bool IsNegotiatedPaidHealthRest(NpcServiceContract contract,ResidentActor actor,
            NpcTaskPlan task,double now)
        {
            if(contract==null || contract.RestTreatment!=NpcServiceRestTreatment.CountsAsPaidAvailability ||
                (contract.Coverage & NpcServiceCoverage.Rest)==0 || task==null || task.Steps==null ||
                task.Steps.Count!=1 || task.Steps[0]==null || task.Steps[0].Kind!=NpcActionKind.RestToHeal)
                return false;
            int cost,gain;double seconds;
            return TryGetHealthRestChoice(actor,task,now,out cost,out gain,out seconds);
        }

        // Persist a pause before cancelling the previous task. A paused episode can retry the same transition.
        private bool PrepareHealthServiceInterruption(HealthServiceChoice choice,string personId,
            NpcHealthCareRecord care,NpcVitalsView vitals,NpcArchetype archetype,
            NpcGoalArbitrationResult selection,ResidentActor actor,NpcTaskPlan proposed,double now)
        {
            if(choice==null || choice.Ambiguous || !worldStore.IsCurrentNpcServiceContractWriteTicket(choice.Ticket))return false;
            if(choice.Contract==null)return true;
            if(choice.Admission==NpcGoalServiceHealthAdmission.PaidRest)
                return choice.Contract.Status==NpcServiceContractStatus.Active &&
                    IsNegotiatedPaidHealthRest(choice.Contract,actor,proposed,now);
            if(choice.Admission!=NpcGoalServiceHealthAdmission.RequiresPause)return false;
            var currentCare=worldStore.ReadNpcHealthCare(personId);
            var currentVitals=worldStore.ReadNpcVitals(personId,archetype);
            if(currentCare==null || currentCare.Id!=care.Id || currentCare.Revision!=care.Revision ||
                currentVitals.Revision!=vitals.Revision || currentVitals.CapturedAt!=vitals.CapturedAt)return false;
            string error;
            if(choice.Contract.Status==NpcServiceContractStatus.Suspended &&
                choice.Contract.SuspensionOwnerKind==NpcServiceSuspensionOwnerKind.HealthCare &&
                choice.Contract.SuspensionOwnerId!=care.Id)
            {
                if(!worldStore.TryTransferNpcServiceHealthPauseToNewCare(choice.Ticket,choice.Contract.ContractId,
                    personId,choice.Contract.SuspensionOwnerId,care.Id,care.Revision,archetype,
                    vitals.Revision,vitals.CapturedAt,selection.MotivationRevision,
                    selection.GoalStartRevision,now,out error))return false;
                choice.Ticket=worldStore.CaptureNpcServiceContractWriteTicket();
            }
            bool newlyPaused;
            return worldStore.TryEnsureNpcServiceHealthCarePause(choice.Ticket,choice.Contract.ContractId,
                personId,care.Id,care.Revision,archetype,vitals.Revision,vitals.CapturedAt,
                selection.MotivationRevision,selection.GoalStartRevision,now,out newlyPaused,out error);
        }

        // Called after native vitals capture, and before a new care episode can replace a recovered one.
        private void ReconcileHealthService(Plan plan,ResidentActor actor,NpcHealthCareRecord care,
            NpcVitalsView vitals,NpcArchetype archetype,double now)
        {
            if(care==null)return;
            var ticket=worldStore.CaptureNpcServiceContractWriteTicket();
            var expired=new List<string>();
            foreach(var contract in ticket.SourceLedger.Contracts)
                if(contract.NpcId==care.PersonId && now>=contract.BlockEndAt &&
                    (contract.Status==NpcServiceContractStatus.Active ||
                     contract.Status==NpcServiceContractStatus.Suspended &&
                     contract.SuspensionOwnerKind==NpcServiceSuspensionOwnerKind.HealthCare))
                    expired.Add(contract.ContractId);
            foreach(var contractId in expired)TrySettleExpiredHealthService(care.PersonId,actor,contractId,now);

            if(care.Phase!=NpcHealthCarePhase.Recovered || vitals==null || !vitals.IsPersisted ||
                !NpcHealthNeedLifecycle.HasRecoveredForWork(vitals) || !plan.Resident.IsAlive ||
                plan.Resident.IsQuestProtected || QuestOwned(plan.Resident) || IsAlarmed(care.PersonId) ||
                worldStore.IsResidentAway(care.PersonId,now))return;
            ticket=worldStore.CaptureNpcServiceContractWriteTicket();
            foreach(var contract in ticket.SourceLedger.Contracts)
            {
                if(contract.NpcId!=care.PersonId || contract.Status!=NpcServiceContractStatus.Suspended ||
                    contract.SuspensionOwnerKind!=NpcServiceSuspensionOwnerKind.HealthCare ||
                    contract.SuspensionOwnerId!=care.Id || now>=contract.BlockEndAt)continue;
                string error;
                worldStore.TryResumeNpcServiceFromHealthCare(ticket,contract.ContractId,care.PersonId,
                    care.Id,care.Revision,archetype,vitals.Revision,vitals.CapturedAt,now,out error);
                break;
            }
        }

        private void TrySettleExpiredHealthService(string personId,ResidentActor actor,string contractId,double now)
        {
            if(healthServiceSettlementFatal.Contains(contractId))return;
            float retryAt;
            if(healthServiceSettlementRetryAt.TryGetValue(contractId,out retryAt) && Time.unscaledTime<retryAt)return;
            healthServiceSettlementRetryAt[contractId]=Time.unscaledTime+10f;
            if(GameManager.Instance==null || GameManager.Instance.PlayerEntity==null)return;
            string error;
            var owner=actor?actor.GetComponent<NativeNpcInventoryOwner>():null;
            if(owner && owner.Binding!=null && !NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out error))
            {Debug.LogError("NPC service settlement inventory capture failed: "+error);return;}
            NativeNpcInventoryBinding binding;
            if(!NativeNpcInventoryBridge.TryOpen(worldStore,personId,null,
                NativeNpcInventoryBridge.InitialWalletGold(worldStore,personId),out binding,out error))
            {Debug.LogError("NPC service settlement inventory open failed: "+error);return;}
            var ticket=worldStore.CaptureNpcServiceContractWriteTicket();
            var contract=ticket.SourceLedger.Read(contractId);
            if(contract==null || contract.NpcId!=personId || now<contract.BlockEndAt ||
                contract.Status!=NpcServiceContractStatus.Active &&
                (contract.Status!=NpcServiceContractStatus.Suspended ||
                 contract.SuspensionOwnerKind!=NpcServiceSuspensionOwnerKind.HealthCare))return;
            NpcServiceContractLedger committed;
            NpcServiceTransferReceipt earnings,refund;
            Exception failure;
            if(NativeNpcServiceContractSettlement.TrySettle(worldStore,ticket,
                GameManager.Instance.PlayerEntity,contractId,contract.Revision,NpcServiceEndReason.NaturalExpiry,
                out committed,out earnings,out refund,out failure))
            {healthServiceSettlementRetryAt.Remove(contractId);return;}
            if(failure is AggregateException)healthServiceSettlementFatal.Add(contractId);
            Debug.LogError("NPC service settlement failed for "+contractId+": "+failure);
        }
    }
}
