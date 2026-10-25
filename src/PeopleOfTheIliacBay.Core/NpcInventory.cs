using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public static class NpcInventoryLimits
    {
        public const int MaximumItems=512;
        public const int MaximumItemPayloadLength=65536;
        public const int MaximumInventoryPayloadLength=2097152;
        public const int MaximumWalletGold=1000000000;
        public const long MaximumRevision=1000000000000;

        public static bool CanAppendExactItem(IList<NpcInventoryItem> existing,NpcInventoryItem incoming)
        {
            if(existing==null || incoming==null || existing.Count>=MaximumItems || incoming.Uid==0 ||
                string.IsNullOrWhiteSpace(incoming.NativePayload) || incoming.NativePayload.Length>MaximumItemPayloadLength) return false;
            var ids=new HashSet<ulong>();long length=incoming.NativePayload.Length;
            foreach(var item in existing)
            {
                if(item==null || item.Uid==0 || !ids.Add(item.Uid) || string.IsNullOrWhiteSpace(item.NativePayload) ||
                    item.NativePayload.Length>MaximumItemPayloadLength) return false;
                if(item.Uid==incoming.Uid) return false;
                length+=item.NativePayload.Length;
                if(length>MaximumInventoryPayloadLength) return false;
            }
            return length<=MaximumInventoryPayloadLength;
        }
    }
    // NativePayload is the complete native ItemData_v1 serialization, not a
    // reconstruction from a template. Unknown custom classes remain opaque.
    [Serializable] public sealed class NpcInventoryItem {public ulong Uid;public string NativePayload;}
    [Serializable] public sealed class NpcInventoryState
    {
        public string PersonId;
        public int WalletGold;
        public long Revision;
        public string LastTransactionId;
        public List<NpcInventoryItem> Items=new List<NpcInventoryItem>();
    }
    public sealed class NpcInventoryUpdate
    {
        public string PersonId;
        public long ExpectedRevision;
        public int WalletGold;
        public List<NpcInventoryItem> Items=new List<NpcInventoryItem>();
    }
    public sealed partial class WorldStateStore
    {
        private WorldState npcInventoryIndexOwner;
        private Dictionary<string,NpcInventoryState> npcInventoryIndex;
        private Dictionary<ulong,string> npcItemOwners;
        private NpcInventoryState IndexedNpcInventory(string id)
        {
            if(npcInventoryIndexOwner!=state)
            {
                npcInventoryIndex=new Dictionary<string,NpcInventoryState>(StringComparer.Ordinal);
                npcItemOwners=new Dictionary<ulong,string>();
                foreach(var value in state.NpcInventories)
                {npcInventoryIndex.Add(value.PersonId,value);foreach(var item in value.Items)npcItemOwners.Add(item.Uid,value.PersonId);}
                npcInventoryIndexOwner=state;
            }
            NpcInventoryState found;return id!=null && npcInventoryIndex.TryGetValue(id,out found)?found:null;
        }
        public NpcInventoryState ReadNpcInventory(string personId)
        {var current=IndexedNpcInventory(personId);return current==null?null:CopyNpcInventory(current);}
        // Presence check for lazy first-materialization. Avoid copying every saved
        // item record when caller only needs to distinguish missing from empty.
        public bool HasNpcInventory(string personId)
        {return IndexedNpcInventory(personId)!=null;}
        public string NpcInventoryItemOwner(ulong uid)
        {IndexedNpcInventory(null);string owner;return npcItemOwners.TryGetValue(uid,out owner)?owner:null;}
        public string CorpseInventoryOwner(string incidentId)
        {
            var incident=state.AlarmIncidents.Find(a=>a.Id==incidentId);
            return incident==null || incident.Corpse==null || incident.Corpse.Cleared?null:incident.VictimId;
        }
        public bool InitializeNpcInventory(string personId,int walletGold,IEnumerable<NpcInventoryItem> items)
        {
            Require(items!=null && walletGold>=0 && walletGold<=NpcInventoryLimits.MaximumWalletGold,"Invalid initial NPC inventory.");
            bool alive;if(!KnownNpcIdentity(state,personId,out alive) || IndexedNpcInventory(personId)!=null)return false;
            var proposed=new NpcInventoryState{PersonId=personId,WalletGold=walletGold,Items=new List<NpcInventoryItem>(items)};
            ValidateNpcInventory(proposed,new HashSet<ulong>());
            foreach(var item in proposed.Items)if(npcItemOwners.ContainsKey(item.Uid))return false;
            proposed=CopyNpcInventory(proposed);state.NpcInventories.Add(proposed);npcInventoryIndex.Add(personId,proposed);
            foreach(var item in proposed.Items)npcItemOwners.Add(item.Uid,personId);return true;
        }
        // Callers retain all expected revisions with their operation. A replay
        // after another transaction fails even once the compact receipt changes.
        public bool CommitNpcInventoryTransaction(string transactionId,IList<NpcInventoryUpdate> updates)
        {
            if(string.IsNullOrWhiteSpace(transactionId) || transactionId.Length>240 || updates==null || updates.Count<1 || updates.Count>8)return false;
            var owners=new HashSet<string>(StringComparer.Ordinal);var proposals=new List<NpcInventoryState>();var uids=new HashSet<ulong>();
            foreach(var update in updates)
            {
                if(update==null || string.IsNullOrWhiteSpace(update.PersonId) || !owners.Add(update.PersonId))return false;
                var current=IndexedNpcInventory(update.PersonId);
                if(current==null || update.ExpectedRevision!=current.Revision || current.Revision>=NpcInventoryLimits.MaximumRevision || current.LastTransactionId==transactionId)return false;
                var proposed=new NpcInventoryState{PersonId=update.PersonId,WalletGold=update.WalletGold,Revision=current.Revision+1,LastTransactionId=transactionId,Items=update.Items};
                try {ValidateNpcInventory(proposed,uids);proposals.Add(CopyNpcInventory(proposed));}
                catch(ArgumentException){return false;}
            }
            foreach(var proposed in proposals)foreach(var item in proposed.Items)
            {string owner;if(npcItemOwners.TryGetValue(item.Uid,out owner) && !owners.Contains(owner))return false;}
            // Every check and detached copy completes before the first write.
            foreach(var proposed in proposals)foreach(var item in npcInventoryIndex[proposed.PersonId].Items)npcItemOwners.Remove(item.Uid);
            foreach(var proposed in proposals)
            {
                var old=npcInventoryIndex[proposed.PersonId];state.NpcInventories[state.NpcInventories.IndexOf(old)]=proposed;npcInventoryIndex[proposed.PersonId]=proposed;
                foreach(var item in proposed.Items)npcItemOwners.Add(item.Uid,proposed.PersonId);
            }
            return true;
        }
        public bool TryPayNpcService(string personId,int gold,string transactionId,long expectedRevision)
        {
            var current=ReadNpcInventory(personId);bool alive;
            if(gold<=0 || current==null || gold>current.WalletGold || !KnownNpcIdentity(state,personId,out alive) || !alive)return false;
            return CommitNpcInventoryTransaction(transactionId,new[]{new NpcInventoryUpdate{PersonId=personId,ExpectedRevision=expectedRevision,
                WalletGold=current.WalletGold-gold,Items=current.Items}});
        }
        private static List<NpcInventoryState> CopyNpcInventories(WorldState source,WorldState validated)
        {
            Require(source.NpcInventories!=null,"Missing NPC inventory ledger.");
            var result=new List<NpcInventoryState>();var owners=new HashSet<string>(StringComparer.Ordinal);var uids=new HashSet<ulong>();
            var known=new HashSet<string>(StringComparer.Ordinal);
            foreach(var town in validated.Settlements)foreach(var home in town.Households)foreach(var person in home.Residents)known.Add(person.Id);
            foreach(var history in validated.HouseholdHistory)foreach(var person in history.Household.Residents)known.Add(person.Id);
            foreach(var person in validated.NativePeople)known.Add(person.Id);
            foreach(var person in validated.Travelers)known.Add(person.Id);
            foreach(var value in source.NpcInventories)
            {
                Require(value!=null && owners.Add(value.PersonId) && known.Contains(value.PersonId),"Unknown or duplicate NPC inventory owner.");
                ValidateNpcInventory(value,uids);result.Add(CopyNpcInventory(value));
            }
            return result;
        }
        private static void ValidateNpcInventory(NpcInventoryState value,HashSet<ulong> uids)
        {
            Require(!string.IsNullOrWhiteSpace(value.PersonId) && value.PersonId.Length<=240 && value.WalletGold>=0 && value.WalletGold<=NpcInventoryLimits.MaximumWalletGold &&
                value.Revision>=0 && value.Revision<=NpcInventoryLimits.MaximumRevision && value.Items!=null && value.Items.Count<=NpcInventoryLimits.MaximumItems &&
                (value.Revision==0?value.LastTransactionId==null:!string.IsNullOrWhiteSpace(value.LastTransactionId) && value.LastTransactionId.Length<=240),"Invalid NPC inventory record.");
            long length=0;
            foreach(var item in value.Items)
            {
                Require(item!=null && item.Uid>0 && uids.Add(item.Uid) && !string.IsNullOrWhiteSpace(item.NativePayload) && item.NativePayload.Length<=NpcInventoryLimits.MaximumItemPayloadLength,"Invalid or duplicate native inventory item.");
                length+=item.NativePayload.Length;Require(length<=NpcInventoryLimits.MaximumInventoryPayloadLength,"NPC inventory payload too large.");
            }
        }
        private static NpcInventoryState CopyNpcInventory(NpcInventoryState value)
        {
            var result=new NpcInventoryState{PersonId=value.PersonId,WalletGold=value.WalletGold,Revision=value.Revision,LastTransactionId=value.LastTransactionId};
            foreach(var item in value.Items)result.Items.Add(new NpcInventoryItem{Uid=item.Uid,NativePayload=item.NativePayload});return result;
        }
    }
}
