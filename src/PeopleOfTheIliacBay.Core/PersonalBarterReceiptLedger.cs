using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class PersonalBarterReceiptLine
    {
        public PersonalBarterDirection Direction;
        public ulong SourceItemUid, RecipientItemUid;
        public int Quantity;
        public LoanPurpose LoanPurpose;
        public string OriginReceiptId;
        public string PlayerInventoryTokenAfter;
        // NPC item payload before and after native transfer. A null after value
        // means the NPC source item left its inventory completely.
        public string NpcPayloadBefore, NpcPayloadAfter;

        public PersonalBarterReceiptLine Copy()
        {
            return new PersonalBarterReceiptLine { Direction=Direction, SourceItemUid=SourceItemUid,
                RecipientItemUid=RecipientItemUid, Quantity=Quantity,
                LoanPurpose=LoanPurpose, OriginReceiptId=OriginReceiptId,
                PlayerInventoryTokenAfter=PlayerInventoryTokenAfter,
                NpcPayloadBefore=NpcPayloadBefore, NpcPayloadAfter=NpcPayloadAfter };
        }
    }

    public sealed class PersonalBarterReceipt
    {
        public string TransactionId, NpcId;
        public long ExpectedNpcRevision, NpcInventoryRevisionAfter;
        public int NpcWalletGoldAfter, NetGoldToNpc;
        public string PlayerInventoryTokenAfter, PlayerWalletTokenAfter;
        public List<PersonalBarterReceiptLine> Lines=new List<PersonalBarterReceiptLine>();

        // This binds native observation to all and only NPC changes. Player
        // inventory remains native-owned; stable player tokens bind that side.
        public bool MatchesNpcUpdate(NpcInventoryState before,NpcInventoryUpdate update)
        {
            if(!ValidReceipt(this) || before==null || update==null || before.PersonId!=NpcId ||
                update.PersonId!=NpcId || before.Revision!=ExpectedNpcRevision ||
                update.ExpectedRevision!=ExpectedNpcRevision || before.WalletGold<0 ||
                update.WalletGold!=NpcWalletGoldAfter || (long)before.WalletGold+NetGoldToNpc!=NpcWalletGoldAfter ||
                NpcInventoryRevisionAfter!=ExpectedNpcRevision+1 ||
                before.Items==null || update.Items==null)return false;
            var oldItems=Items(before.Items);var newItems=Items(update.Items);
            if(oldItems==null || newItems==null)return false;
            var changed=new HashSet<ulong>();
            foreach(var line in Lines)
            {
                if(line.Direction==PersonalBarterDirection.NpcToPlayer)
                {
                    string oldPayload;
                    if(!oldItems.TryGetValue(line.SourceItemUid,out oldPayload) || oldPayload!=line.NpcPayloadBefore ||
                        !changed.Add(line.SourceItemUid))return false;
                    string nextPayload;
                    if(line.NpcPayloadAfter==null)
                    {
                        if(newItems.ContainsKey(line.SourceItemUid))return false;
                    }
                    else if(!newItems.TryGetValue(line.SourceItemUid,out nextPayload) || nextPayload!=line.NpcPayloadAfter)
                        return false;
                }
                else
                {
                    string payload;
                    if(oldItems.ContainsKey(line.RecipientItemUid) ||
                        !newItems.TryGetValue(line.RecipientItemUid,out payload) || payload!=line.NpcPayloadAfter ||
                        !changed.Add(line.RecipientItemUid))return false;
                }
            }
            foreach(var item in oldItems)
                if(!changed.Contains(item.Key) && (!newItems.ContainsKey(item.Key) || newItems[item.Key]!=item.Value))return false;
            foreach(var item in newItems)
                if(!changed.Contains(item.Key) && (!oldItems.ContainsKey(item.Key) || oldItems[item.Key]!=item.Value))return false;
            return true;
        }

        internal static bool ValidReceipt(PersonalBarterReceipt receipt)
        {
            if(receipt==null || !Id(receipt.TransactionId,240) || !Id(receipt.NpcId,240) ||
                receipt.ExpectedNpcRevision<0 || receipt.ExpectedNpcRevision>=NpcInventoryLimits.MaximumRevision ||
                receipt.NpcInventoryRevisionAfter!=receipt.ExpectedNpcRevision+1 ||
                receipt.NpcWalletGoldAfter<0 || receipt.NpcWalletGoldAfter>NpcInventoryLimits.MaximumWalletGold ||
                !Id(receipt.PlayerInventoryTokenAfter,1024) || !Id(receipt.PlayerWalletTokenAfter,1024) ||
                receipt.Lines==null || receipt.Lines.Count<1 || receipt.Lines.Count>PersonalBarterContract.MaximumLines)return false;
            var sourceIds=new HashSet<ulong>();var recipientIds=new HashSet<ulong>();
            foreach(var line in receipt.Lines)
            {
                if(line==null || !Enum.IsDefined(typeof(PersonalBarterDirection),line.Direction) ||
                    line.SourceItemUid==0 || line.RecipientItemUid==0 || line.Quantity<1 || line.Quantity>1000000 ||
                    !ValidLoan(line) || !Id(line.PlayerInventoryTokenAfter,1024) ||
                    !sourceIds.Add(line.SourceItemUid) || !recipientIds.Add(line.RecipientItemUid))return false;
                if(line.Direction==PersonalBarterDirection.NpcToPlayer)
                {
                    if(!Payload(line.NpcPayloadBefore))return false;
                    // A full move retains UID and removes source. A native split
                    // has a distinct recipient UID and retains exact source payload.
                    if(line.RecipientItemUid==line.SourceItemUid)
                    { if(line.NpcPayloadAfter!=null)return false; }
                    else if(!Payload(line.NpcPayloadAfter))return false;
                }
                else
                {
                    if(line.NpcPayloadBefore!=null || !Payload(line.NpcPayloadAfter))return false;
                }
            }
            return true;
        }

        private static Dictionary<ulong,string> Items(IList<NpcInventoryItem> items)
        {
            var found=new Dictionary<ulong,string>();
            foreach(var item in items)
            {
                if(item==null || item.Uid==0 || !Payload(item.NativePayload) || found.ContainsKey(item.Uid))return null;
                found.Add(item.Uid,item.NativePayload);
            }
            return found;
        }
        private static bool Payload(string value)
        { return !string.IsNullOrWhiteSpace(value) && value.Length<=NpcInventoryLimits.MaximumItemPayloadLength; }
        private static bool ValidLoan(PersonalBarterReceiptLine line)
        {
            if(!Enum.IsDefined(typeof(LoanPurpose),line.LoanPurpose))return false;
            if(line.LoanPurpose==LoanPurpose.None)return line.OriginReceiptId==null;
            if(line.LoanPurpose==LoanPurpose.Lend)
                return line.Direction==PersonalBarterDirection.PlayerToNpc && line.OriginReceiptId==null;
            return line.Direction==PersonalBarterDirection.NpcToPlayer && Id(line.OriginReceiptId,240);
        }
        private static bool Id(string value,int maximum)
        { return !string.IsNullOrWhiteSpace(value) && value.Length<=maximum; }
    }

    [Serializable]
    public sealed class PersonalBarterReceiptLedgerEntry
    {
        public long CommitRevision;
        public string ReceiptId, EffectId;
        public bool MercantileTallyReserved;
        public double CommittedGameTime;
        public PersonalBarterReceipt Receipt;
        public PersonalBarterReceiptLedgerEntry Copy()
        { return new PersonalBarterReceiptLedgerEntry { CommitRevision=CommitRevision, ReceiptId=ReceiptId,
            EffectId=EffectId, MercantileTallyReserved=MercantileTallyReserved,
            CommittedGameTime=CommittedGameTime, Receipt=CopyReceipt(Receipt) }; }
        internal static PersonalBarterReceipt CopyReceipt(PersonalBarterReceipt source)
        {
            if(source==null)return null;
            var copy=new PersonalBarterReceipt { TransactionId=source.TransactionId,NpcId=source.NpcId,
                ExpectedNpcRevision=source.ExpectedNpcRevision,NpcInventoryRevisionAfter=source.NpcInventoryRevisionAfter,
                NpcWalletGoldAfter=source.NpcWalletGoldAfter,NetGoldToNpc=source.NetGoldToNpc,
                PlayerInventoryTokenAfter=source.PlayerInventoryTokenAfter,PlayerWalletTokenAfter=source.PlayerWalletTokenAfter };
            if(source.Lines==null) { copy.Lines=null; return copy; }
            foreach(var line in source.Lines)copy.Lines.Add(line==null?null:line.Copy());
            return copy;
        }
    }

    [Serializable]
    public sealed class PersonalBarterReceiptLedgerState
    {
        public const int CurrentVersion=1;
        public int Version=CurrentVersion;
        public string SaveBranchId;
        public long Revision;
        public List<PersonalBarterReceiptLedgerEntry> Entries=new List<PersonalBarterReceiptLedgerEntry>();
        public PersonalBarterReceiptLedgerState Copy()
        {
            var copy=new PersonalBarterReceiptLedgerState { Version=Version,SaveBranchId=SaveBranchId,Revision=Revision,
                Entries=new List<PersonalBarterReceiptLedgerEntry>() };
            if(Entries==null)copy.Entries=null; else foreach(var entry in Entries)copy.Entries.Add(entry==null?null:entry.Copy());
            return copy;
        }
    }

    public sealed class PersonalBarterReceiptLedger
    {
        public const int MaximumEntries=100000;
        private readonly string branchId;
        private readonly List<PersonalBarterReceiptLedgerEntry> entries;
        private long revision;
        public PersonalBarterReceiptLedger(string saveBranchId)
        {
            if(!Id(saveBranchId,128))throw new ArgumentException("Invalid barter save branch.",nameof(saveBranchId));
            branchId=saveBranchId;entries=new List<PersonalBarterReceiptLedgerEntry>();
        }
        private PersonalBarterReceiptLedger(PersonalBarterReceiptLedgerState state)
        { branchId=state.SaveBranchId;revision=state.Revision;entries=new List<PersonalBarterReceiptLedgerEntry>();foreach(var entry in state.Entries)entries.Add(entry.Copy()); }
        public PersonalBarterReceiptLedgerState Snapshot()
        { var snapshot=new PersonalBarterReceiptLedgerState { SaveBranchId=branchId,Revision=revision };foreach(var entry in entries)snapshot.Entries.Add(entry.Copy());return snapshot; }
        public static bool TryRestore(PersonalBarterReceiptLedgerState saved,string expectedBranchId,out PersonalBarterReceiptLedger restored)
        { restored=null;if(!Validate(saved,expectedBranchId))return false;restored=new PersonalBarterReceiptLedger(saved);return true; }
        public static bool Validate(PersonalBarterReceiptLedgerState saved,string expectedBranchId)
        {
            if(saved==null || saved.Version!=PersonalBarterReceiptLedgerState.CurrentVersion || !Id(expectedBranchId,128) ||
                saved.SaveBranchId!=expectedBranchId || saved.Revision<0 || saved.Entries==null || saved.Entries.Count>MaximumEntries)return false;
            var transactions=new HashSet<string>();var receipts=new HashSet<string>();var effects=new HashSet<string>();
            var npcRevisions=new Dictionary<string,long>();long previous=0;double time=0;
            foreach(var entry in saved.Entries)
            {
                if(entry==null || entry.CommitRevision!=previous+1 || entry.CommitRevision>saved.Revision ||
                    !Id(entry.ReceiptId,240) || !Id(entry.EffectId,240) || !PersonalBarterReceipt.ValidReceipt(entry.Receipt) ||
                    !transactions.Add(entry.Receipt.TransactionId) || !receipts.Add(entry.ReceiptId) || !effects.Add(entry.EffectId) ||
                    double.IsNaN(entry.CommittedGameTime) || double.IsInfinity(entry.CommittedGameTime) || entry.CommittedGameTime<time)return false;
                long prior;if(npcRevisions.TryGetValue(entry.Receipt.NpcId,out prior) && entry.Receipt.ExpectedNpcRevision<prior)return false;
                npcRevisions[entry.Receipt.NpcId]=entry.Receipt.NpcInventoryRevisionAfter;previous=entry.CommitRevision;time=entry.CommittedGameTime;
            }
            return previous==saved.Revision;
        }
        public bool TryCommit(string expectedBranchId,PersonalBarterReceipt receipt,string receiptId,string effectId,double committedGameTime)
        {
            if(expectedBranchId!=branchId || !PersonalBarterReceipt.ValidReceipt(receipt) || !Id(receiptId,240) || !Id(effectId,240) ||
                double.IsNaN(committedGameTime) || double.IsInfinity(committedGameTime) || committedGameTime<0 || entries.Count>=MaximumEntries || revision==long.MaxValue ||
                entries.Count>0 && committedGameTime<entries[entries.Count-1].CommittedGameTime)return false;
            foreach(var prior in entries)
                if(prior.Receipt.TransactionId==receipt.TransactionId || prior.ReceiptId==receiptId || prior.EffectId==effectId ||
                    prior.Receipt.NpcId==receipt.NpcId && prior.Receipt.NpcInventoryRevisionAfter>=receipt.NpcInventoryRevisionAfter)return false;
            entries.Add(new PersonalBarterReceiptLedgerEntry { CommitRevision=++revision,ReceiptId=receiptId,EffectId=effectId,
                CommittedGameTime=committedGameTime,Receipt=PersonalBarterReceiptLedgerEntry.CopyReceipt(receipt) });return true;
        }
        public bool TryReserveMercantileTally(string expectedBranchId,string transactionId)
        {
            if(expectedBranchId!=branchId || !Id(transactionId,240))return false;
            foreach(var entry in entries)
            {
                if(entry.Receipt==null || entry.Receipt.TransactionId!=transactionId || entry.MercantileTallyReserved ||
                    !EligibleForMercantile(entry.Receipt))continue;
                entry.MercantileTallyReserved=true;
                return true;
            }
            return false;
        }
        private static bool EligibleForMercantile(PersonalBarterReceipt receipt)
        {
            if(receipt==null || receipt.Lines==null || receipt.Lines.Count==0)return false;
            foreach(var line in receipt.Lines)if(line==null || line.LoanPurpose!=LoanPurpose.None)return false;
            return true;
        }
        private static bool Id(string value,int maximum)
        { return !string.IsNullOrWhiteSpace(value) && value.Length<=maximum; }
    }
}
