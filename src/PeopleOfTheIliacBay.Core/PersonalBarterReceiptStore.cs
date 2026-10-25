using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        public PersonalBarterReceiptLedgerState ReadPersonalBarterReceipts()
        { return state.PersonalBarterReceipts.Copy(); }

        // One state swap publishes the complete NPC basket and its replay proof.
        // A rejected commit leaves native caller responsible for exact rollback.
        public bool TryCommitPersonalBarter(NpcInventoryUpdate update,
            PersonalBarterReceipt receipt,string receiptId,string effectId,double committedGameTime)
        {
            if(update==null || receipt==null ||
                !PersonalBarterReceiptLedger.TryRestore(state.PersonalBarterReceipts,
                    state.NpcFrameworkScopeId,out var ledger))return false;
            var current=IndexedNpcInventory(update.PersonId);
            if(current==null || current.LastTransactionId==receipt.TransactionId ||
                current.Revision>=NpcInventoryLimits.MaximumRevision ||
                !receipt.MatchesNpcUpdate(current,update) ||
                !ledger.TryCommit(state.NpcFrameworkScopeId,receipt,receiptId,effectId,committedGameTime))return false;
            var proposed=new NpcInventoryState { PersonId=update.PersonId,WalletGold=update.WalletGold,
                Revision=current.Revision+1,LastTransactionId=receipt.TransactionId,Items=update.Items };
            try { ValidateNpcInventory(proposed,new HashSet<ulong>()); }
            catch(ArgumentException) { return false; }
            foreach(var item in proposed.Items)
            {
                string owner;
                if(npcItemOwners.TryGetValue(item.Uid,out owner) && owner!=proposed.PersonId)return false;
            }
            var detachedInventory=CopyNpcInventory(proposed);
            var detachedLedger=ledger.Snapshot();
            if(!PersonalBarterReceiptLedger.Validate(detachedLedger,state.NpcFrameworkScopeId))return false;
            var candidate=state.CandidateForPersonalBarter();
            int index=candidate.NpcInventories.IndexOf(current);
            if(index<0)return false;
            candidate.NpcInventories[index]=detachedInventory;
            candidate.PersonalBarterReceipts=detachedLedger;
            state=candidate;
            return true;
        }

        public bool TryReservePersonalBarterMercantileTally(string transactionId)
        {
            if(!PersonalBarterReceiptLedger.TryRestore(state.PersonalBarterReceipts,
                state.NpcFrameworkScopeId,out var ledger) ||
                !ledger.TryReserveMercantileTally(state.NpcFrameworkScopeId,transactionId))return false;
            var next=ledger.Snapshot();
            if(!PersonalBarterReceiptLedger.Validate(next,state.NpcFrameworkScopeId))return false;
            var candidate=state.CandidateForPersonalBarter();
            candidate.PersonalBarterReceipts=next;
            state=candidate;
            return true;
        }
    }
}
