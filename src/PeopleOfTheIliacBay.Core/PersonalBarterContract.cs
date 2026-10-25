using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // A personal basket is an exact buy, sale, or mixed barter. A one-way
    // zero-gold transfer must carry durable lending or return provenance.
    public enum PersonalBarterDirection { NpcToPlayer, PlayerToNpc }

    public sealed class PersonalBarterLine
    {
        public PersonalBarterDirection Direction;
        public ulong ItemUid;
        public int Quantity;
        public LoanPurpose LoanPurpose;
        public string OriginReceiptId;
        // Player-owned source lines must bind exact current native stack.
        public string ExpectedPlayerInventoryToken;
    }

    public sealed class PersonalBarterOrder
    {
        public string TransactionId, NpcId;
        public string PlayerInventoryContainerId, PlayerWalletContainerId;
        public string ExpectedPlayerWalletToken;
        public long ExpectedNpcRevision;
        // Positive: player wallet pays NPC. Negative: NPC wallet pays player.
        public int NetGoldToNpc;
        public List<PersonalBarterLine> Lines = new List<PersonalBarterLine>();
    }

    // Native bridge creates this from one live reread. It contains no prices,
    // market simulation, inventory mutation, or saved player inventory copy.
    public sealed class PersonalBarterLineState
    {
        public PersonalBarterDirection Direction;
        public ulong ItemUid;
        public int SourceQuantity, ReceiverCapacity;
        public string PlayerInventoryToken;
        public bool ItemAllowed, ItemValued;
        public bool ItemEquipped, ItemOccupationallyNecessary, ItemUnique, ItemKnownStolen, ItemQuestProtected, ItemCurrency;
    }

    public sealed class PersonalBarterState
    {
        public string NpcId, PlayerInventoryContainerId, PlayerWalletContainerId, PlayerWalletToken;
        public long NpcRevision;
        public int NpcWalletGold, PlayerGold, PlayerMercantile;
        public bool PlayerConsented, NpcConsented, NpcAdult, NpcEligible, NpcWilling;
        public List<PersonalBarterLineState> Lines = new List<PersonalBarterLineState>();
    }

    public static class PersonalBarterContract
    {
        public const int MaximumLines = 24;

        public static bool CanBegin(PersonalBarterOrder order, PersonalBarterState state)
        { return Validate(order, state, true); }

        // An asking price is not a payment. Keep item and identity checks while
        // allowing the parties to negotiate a final price they can afford.
        public static bool CanQuote(PersonalBarterOrder order, PersonalBarterState state)
        { return Validate(order, state, false); }

        private static bool Validate(PersonalBarterOrder order, PersonalBarterState state, bool requirePayment)
        {
            if(order==null || state==null || !Id(order.TransactionId) || !Id(order.NpcId) || order.NpcId=="player" ||
                !Id(order.PlayerInventoryContainerId) || !Id(order.PlayerWalletContainerId) ||
                !Id(order.ExpectedPlayerWalletToken) || order.ExpectedNpcRevision<0 ||
                order.Lines==null || state.Lines==null || order.Lines.Count<1 || order.Lines.Count>MaximumLines ||
                order.Lines.Count!=state.Lines.Count || order.NpcId!=state.NpcId ||
                order.PlayerInventoryContainerId!=state.PlayerInventoryContainerId ||
                order.PlayerWalletContainerId!=state.PlayerWalletContainerId ||
                order.ExpectedPlayerWalletToken!=state.PlayerWalletToken ||
                order.ExpectedNpcRevision!=state.NpcRevision || state.NpcRevision>=NpcInventoryLimits.MaximumRevision ||
                !state.PlayerConsented || !state.NpcConsented || !state.NpcAdult || !state.NpcEligible || !state.NpcWilling ||
                state.PlayerGold<0 || state.PlayerGold>NpcInventoryLimits.MaximumWalletGold ||
                state.NpcWalletGold<0 || state.NpcWalletGold>NpcInventoryLimits.MaximumWalletGold ||
                state.PlayerWalletToken!=PlayerCommerceContract.WalletSnapshotToken("player",state.PlayerWalletContainerId,state.PlayerGold))return false;
            long playerAfter=(long)state.PlayerGold-order.NetGoldToNpc;
            long npcAfter=(long)state.NpcWalletGold+order.NetGoldToNpc;
            if(requirePayment && (playerAfter<0 || playerAfter>NpcInventoryLimits.MaximumWalletGold ||
                npcAfter<0 || npcAfter>NpcInventoryLimits.MaximumWalletGold))return false;
            var seen=new HashSet<ulong>();
            for(int index=0;index<order.Lines.Count;index++)
            {
                var line=order.Lines[index];var current=state.Lines[index];
                if(line==null || current==null || !Enum.IsDefined(typeof(PersonalBarterDirection),line.Direction) ||
                    line.Direction!=current.Direction || line.ItemUid==0 || line.ItemUid!=current.ItemUid ||
                    line.Quantity<1 || line.Quantity>1000000 || current.SourceQuantity<line.Quantity ||
                    current.ReceiverCapacity<line.Quantity || !current.ItemAllowed || !current.ItemValued ||
                    current.ItemEquipped || current.ItemOccupationallyNecessary || current.ItemUnique ||
                    current.ItemKnownStolen || current.ItemQuestProtected || current.ItemCurrency || !ValidLoan(line) || !seen.Add(line.ItemUid))return false;
                if(line.Direction==PersonalBarterDirection.PlayerToNpc)
                {
                    if(!Id(line.ExpectedPlayerInventoryToken) || line.ExpectedPlayerInventoryToken!=current.PlayerInventoryToken ||
                        current.PlayerInventoryToken!=PlayerCommerceContract.InventorySnapshotToken("player",
                            state.PlayerInventoryContainerId,line.ItemUid,current.SourceQuantity))return false;
                }
                else
                {
                    if(line.ExpectedPlayerInventoryToken!=null || current.PlayerInventoryToken!=null)return false;
                }
            }
            // Real zero-value items can move without gold. Item eligibility,
            // consent, source ownership and loan receipts are still checked above.
            return true;
        }

        private static bool Id(string value) { return !string.IsNullOrWhiteSpace(value) && value.Length<=240; }
        private static bool ValidLoan(PersonalBarterLine line)
        {
            if(!Enum.IsDefined(typeof(LoanPurpose),line.LoanPurpose))return false;
            if(line.LoanPurpose==LoanPurpose.None)return line.OriginReceiptId==null;
            if(line.LoanPurpose==LoanPurpose.Lend)
                return line.Direction==PersonalBarterDirection.PlayerToNpc && line.OriginReceiptId==null;
            return line.Direction==PersonalBarterDirection.NpcToPlayer && Id(line.OriginReceiptId);
        }
    }
}
