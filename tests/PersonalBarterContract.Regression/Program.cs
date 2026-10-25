using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        try { QuoteDoesNotRequireBaselineFunds(); Console.WriteLine("PASS quote and payment are separate"); }
        catch(Exception error) { Console.Error.WriteLine("FAIL quote and payment are separate: "+error.Message); return 1; }
        try { MixedLinesNeedOneAtomicBasket(); Console.WriteLine("PASS mixed basket"); }
        catch(Exception error) { Console.Error.WriteLine("FAIL mixed basket: "+error.Message); return 1; }
        try { DuplicateUidAndGoldItemsReject(); Console.WriteLine("PASS unsafe lines"); }
        catch(Exception error) { Console.Error.WriteLine("FAIL unsafe lines: "+error.Message); return 1; }
        try { ReceiptLedgerBindsReplayAndNativeDelta(); Console.WriteLine("PASS receipt ledger"); }
        catch(Exception error) { Console.Error.WriteLine("FAIL receipt ledger: "+error.Message); return 1; }
        try { AtomicStoreBasket(); Console.WriteLine("PASS atomic store basket"); }
        catch(Exception error) { Console.Error.WriteLine("FAIL atomic store basket: "+error.Message); return 1; }
        try { ApprovedHaggleNetBindsWalletReceipt(); Console.WriteLine("PASS approved haggled nets"); }
        catch(Exception error) { Console.Error.WriteLine("FAIL approved haggled nets: "+error.Message); return 1; }
        try { SingleDirectionBasketNeedsRealItem(); Console.WriteLine("PASS one-side basket"); }
        catch(Exception error) { Console.Error.WriteLine("FAIL one-side basket: "+error.Message); return 1; }
        try { NpcSplitReceiptRetainsSourceAndNewPlayerUid(); Console.WriteLine("PASS native split receipt"); }
        catch(Exception error) { Console.Error.WriteLine("FAIL native split receipt: "+error.Message); return 1; }
        try { BasketMercantileTallyReservesOnce(); Console.WriteLine("PASS basket Mercantile tally"); }
        catch(Exception error) { Console.Error.WriteLine("FAIL basket Mercantile tally: "+error.Message); return 1; }
        Console.WriteLine("9/9 passed"); return 0;
    }

    private static void QuoteDoesNotRequireBaselineFunds()
    {
        var quote=typeof(PersonalBarterContract).GetMethod("CanQuote");
        Need(quote!=null,"No quote validation exists independently of payment.");
        var order=Order();var state=State();
        order.NetGoldToNpc=state.PlayerGold+1;
        Need((bool)quote.Invoke(null,new object[]{order,state}),"Unaffordable baseline cannot be negotiated.");
        Need(!PersonalBarterContract.CanBegin(order,state),"Unaffordable final payment must fail.");
        order.NetGoldToNpc=state.PlayerGold;
        Need(PersonalBarterContract.CanBegin(order,state),"Affordable final payment must succeed.");
        state.Lines[0].ItemQuestProtected=true;
        Need(!(bool)quote.Invoke(null,new object[]{order,state}),"Quote validation cannot bypass item safety.");
    }

    private static void MixedLinesNeedOneAtomicBasket()
    {
        var order=Order();var state=State();
        Need(PersonalBarterContract.CanBegin(order,state),"valid two-way basket rejected");
        order.Lines[1].ExpectedPlayerInventoryToken="stale";
        Need(!PersonalBarterContract.CanBegin(order,state),"stale player source token admitted");
    }

    private static void DuplicateUidAndGoldItemsReject()
    {
        var order=Order();var state=State();
        order.Lines[1].ItemUid=71;
        Need(!PersonalBarterContract.CanBegin(order,state),"one UID crossed both ways");
        order=Order();state.Lines[0].ItemCurrency=true;
        Need(!PersonalBarterContract.CanBegin(order,state),"gold item entered basket");
    }

    private static void ReceiptLedgerBindsReplayAndNativeDelta()
    {
        var receipt=new PersonalBarterReceipt {
            TransactionId="barter:receipt",NpcId="npc:1",ExpectedNpcRevision=4,
            NpcInventoryRevisionAfter=5,NpcWalletGoldAfter=33,NetGoldToNpc=3,
            PlayerInventoryTokenAfter="player-items:after",PlayerWalletTokenAfter="player-wallet:after",
            Lines=new List<PersonalBarterReceiptLine> {
                new PersonalBarterReceiptLine { Direction=PersonalBarterDirection.NpcToPlayer,
                    SourceItemUid=71,RecipientItemUid=71,Quantity=1,PlayerInventoryTokenAfter="player:71",
                    NpcPayloadBefore="npc-old",NpcPayloadAfter=null },
                new PersonalBarterReceiptLine { Direction=PersonalBarterDirection.PlayerToNpc,
                    SourceItemUid=81,RecipientItemUid=91,Quantity=2,PlayerInventoryTokenAfter="player:81",
                    NpcPayloadAfter="player-new" }
            }
        };
        var before=new NpcInventoryState { PersonId="npc:1",Revision=4,WalletGold=30,
            LastTransactionId="old",Items=new List<NpcInventoryItem> { new NpcInventoryItem {Uid=71,NativePayload="npc-old"} } };
        var update=new NpcInventoryUpdate { PersonId="npc:1",ExpectedRevision=4,WalletGold=33,
            Items=new List<NpcInventoryItem> { new NpcInventoryItem {Uid=91,NativePayload="player-new"} } };
        Need(receipt.MatchesNpcUpdate(before,update),"receipt did not bind exact native basket delta");
        update.Items[0].NativePayload="forged";
        Need(!receipt.MatchesNpcUpdate(before,update),"forged native basket delta admitted");
        update.Items[0].NativePayload="player-new";
        var ledger=new PersonalBarterReceiptLedger("scope");
        Need(ledger.TryCommit("scope",receipt,"receipt:1","effect:1",5),"receipt did not commit");
        Need(!ledger.TryCommit("scope",receipt,"receipt:1","effect:1",5),"replay committed twice");
        Need(PersonalBarterReceiptLedger.Validate(ledger.Snapshot(),"scope"),"valid receipt ledger rejected");
    }

    private static void AtomicStoreBasket()
    {
        var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[] {10},1);
        string npc=town.Households[0].Residents[0].Id;
        Need(store.InitializeNpcInventory(npc,30,new[] { new NpcInventoryItem {Uid=71,NativePayload="npc-old"} }),"fixture rejected");
        var receipt=StoreReceipt(npc);
        var update=new NpcInventoryUpdate { PersonId=npc,ExpectedRevision=0,WalletGold=33,
            Items=new List<NpcInventoryItem> { new NpcInventoryItem {Uid=91,NativePayload="player-new"} } };
        var bad=new NpcInventoryUpdate { PersonId=npc,ExpectedRevision=0,WalletGold=32,Items=update.Items };
        Need(!store.TryCommitPersonalBarter(bad,receipt,"receipt:store","effect:store",9),"wrong wallet committed");
        Need(store.ReadPersonalBarterReceipts().Entries.Count==0,"rejected basket persisted receipt");
        Need(store.TryCommitPersonalBarter(update,receipt,"receipt:store","effect:store",9),"exact basket rejected");
        Need(store.ReadNpcInventory(npc).Revision==1 && store.ReadPersonalBarterReceipts().Entries.Count==1,"atomic basket state missing");
        Need(!store.TryCommitPersonalBarter(update,receipt,"receipt:store","effect:store",9),"basket replay committed");
        var restored=new WorldStateStore(42);restored.Restore(store.Snapshot());
        Need(restored.ReadPersonalBarterReceipts().Entries.Count==1,"saved basket receipt lost");
    }

    private static PersonalBarterReceipt StoreReceipt(string npc)
    {
        return new PersonalBarterReceipt { TransactionId="barter:store",NpcId=npc,ExpectedNpcRevision=0,
            NpcInventoryRevisionAfter=1,NpcWalletGoldAfter=33,NetGoldToNpc=3,
            PlayerInventoryTokenAfter="player-items:after",PlayerWalletTokenAfter="player-wallet:after",
            Lines=new List<PersonalBarterReceiptLine> {
                new PersonalBarterReceiptLine { Direction=PersonalBarterDirection.NpcToPlayer,SourceItemUid=71,
                    RecipientItemUid=71,Quantity=1,PlayerInventoryTokenAfter="player:71",NpcPayloadBefore="npc-old" },
                new PersonalBarterReceiptLine { Direction=PersonalBarterDirection.PlayerToNpc,SourceItemUid=81,
                    RecipientItemUid=91,Quantity=2,PlayerInventoryTokenAfter="player:81",NpcPayloadAfter="player-new" }
            } };
    }

    private static void ApprovedHaggleNetBindsWalletReceipt()
    {
        AssertApprovedNet(10,0,5,20,30);
        AssertApprovedNet(-2,-10,-5,20,30);
    }

    private static void AssertApprovedNet(int nativeBase,int minimum,int current,int playerGold,int npcGold)
    {
        var baseOrder=Order();baseOrder.TransactionId="barter:haggle:"+nativeBase;baseOrder.NetGoldToNpc=nativeBase;
        baseOrder.ExpectedPlayerWalletToken=PlayerCommerceContract.WalletSnapshotToken("player","player-wallet:scope",playerGold);
        var binding=NpcCommerceHaggleBasketBinding.Bind(baseOrder,minimum);
        var round=binding.CreateRound(current,current,1,0,50,0);int approved;
        Need(NpcCommerceHagglingPolicy.TryResolveAcceptedBasketNetGold(binding,baseOrder,nativeBase,round,out approved),"accepted basket net rejected");
        Need(approved==current && approved!=nativeBase,"approved net did not replace native base");
        var committed=Order();committed.TransactionId=baseOrder.TransactionId;committed.NetGoldToNpc=approved;
        committed.ExpectedPlayerWalletToken=baseOrder.ExpectedPlayerWalletToken;
        var state=State();state.PlayerGold=playerGold;state.NpcWalletGold=npcGold;state.PlayerWalletToken=baseOrder.ExpectedPlayerWalletToken;
        Need(PersonalBarterContract.CanBegin(committed,state),"approved net basket rejected");
        var receipt=StoreReceipt("npc:1");receipt.TransactionId=committed.TransactionId;receipt.NpcWalletGoldAfter=npcGold+approved;receipt.NetGoldToNpc=approved;
        var before=new NpcInventoryState { PersonId="npc:1",Revision=0,WalletGold=npcGold,Items=new List<NpcInventoryItem> {new NpcInventoryItem {Uid=71,NativePayload="npc-old"}} };
        var update=new NpcInventoryUpdate { PersonId="npc:1",ExpectedRevision=0,WalletGold=npcGold+approved,Items=new List<NpcInventoryItem> {new NpcInventoryItem {Uid=91,NativePayload="player-new"}} };
        Need(receipt.MatchesNpcUpdate(before,update),"receipt retained native base net instead of approved net");
    }

    private static void SingleDirectionBasketNeedsRealItem()
    {
        var buy=Order();buy.Lines.RemoveAt(1);var buyState=State();buyState.Lines.RemoveAt(1);
        Need(PersonalBarterContract.CanBegin(buy,buyState),"single NPC-to-player basket rejected");
        buy.NetGoldToNpc=0;
        Need(!PersonalBarterContract.CanBegin(buy,buyState),"free one-way non-loan basket admitted");
        buy.Lines[0].LoanPurpose=LoanPurpose.Return;buy.Lines[0].OriginReceiptId="loan:1";
        Need(PersonalBarterContract.CanBegin(buy,buyState),"receipt-bound one-way return rejected");
        var sell=Order();sell.Lines.RemoveAt(0);var sellState=State();sellState.Lines.RemoveAt(0);
        Need(PersonalBarterContract.CanBegin(sell,sellState),"single player-to-NPC basket rejected");
        sell.Lines.Clear();sellState.Lines.Clear();
        Need(!PersonalBarterContract.CanBegin(sell,sellState),"gold-only empty basket admitted");
    }

    private static void NpcSplitReceiptRetainsSourceAndNewPlayerUid()
    {
        var receipt=new PersonalBarterReceipt {TransactionId="barter:split",NpcId="npc:split",ExpectedNpcRevision=0,
            NpcInventoryRevisionAfter=1,NpcWalletGoldAfter=11,NetGoldToNpc=1,
            PlayerInventoryTokenAfter="player-after",PlayerWalletTokenAfter="wallet-after",
            Lines=new List<PersonalBarterReceiptLine> {new PersonalBarterReceiptLine {
                Direction=PersonalBarterDirection.NpcToPlayer,SourceItemUid=71,RecipientItemUid=901,Quantity=1,
                PlayerInventoryTokenAfter="player:901:1",NpcPayloadBefore="stack:2",NpcPayloadAfter="stack:1"}}};
        var before=new NpcInventoryState {PersonId="npc:split",Revision=0,WalletGold=10,
            Items=new List<NpcInventoryItem> {new NpcInventoryItem {Uid=71,NativePayload="stack:2"}}};
        var update=new NpcInventoryUpdate {PersonId="npc:split",ExpectedRevision=0,WalletGold=11,
            Items=new List<NpcInventoryItem> {new NpcInventoryItem {Uid=71,NativePayload="stack:1"}}};
        Need(receipt.MatchesNpcUpdate(before,update),"native two-stack buy-one split receipt rejected");
        receipt.Lines[0].NpcPayloadAfter=null;
        Need(!receipt.MatchesNpcUpdate(before,update),"split receipt without retained source admitted");
    }

    private static void BasketMercantileTallyReservesOnce()
    {
        var store=new WorldStateStore(77);var town=store.GetOrCreate(101,new[] {11},1);string npc=town.Households[0].Residents[0].Id;
        Need(store.InitializeNpcInventory(npc,30,new[] {new NpcInventoryItem {Uid=71,NativePayload="npc-old"}}),"tally fixture rejected");
        var receipt=StoreReceipt(npc);receipt.TransactionId="barter:tally";
        var update=new NpcInventoryUpdate {PersonId=npc,ExpectedRevision=0,WalletGold=33,
            Items=new List<NpcInventoryItem> {new NpcInventoryItem {Uid=91,NativePayload="player-new"}}};
        Need(store.TryCommitPersonalBarter(update,receipt,"receipt:tally","effect:tally",1),"tally basket rejected");
        Need(store.TryReservePersonalBarterMercantileTally("barter:tally"),"committed basket did not reserve Mercantile");
        Need(!store.TryReservePersonalBarterMercantileTally("barter:tally"),"basket Mercantile reserved twice");
        var restored=new WorldStateStore(77);restored.Restore(store.Snapshot());
        Need(!restored.TryReservePersonalBarterMercantileTally("barter:tally"),"restored basket reserved Mercantile twice");
        var loan=StoreReceipt("npc:loan");loan.TransactionId="barter:loan";loan.Lines[0].LoanPurpose=LoanPurpose.Return;
        loan.Lines[0].OriginReceiptId="loan:origin";
        var loanLedger=new PersonalBarterReceiptLedger("scope");
        Need(loanLedger.TryCommit("scope",loan,"receipt:loan","effect:loan",2),"loan receipt rejected");
        Need(!loanLedger.TryReserveMercantileTally("scope","barter:loan"),"loan reserved Mercantile tally");
    }

    private static PersonalBarterOrder Order()
    {
        return new PersonalBarterOrder {
            TransactionId="barter:1",NpcId="npc:1",ExpectedNpcRevision=4,
            PlayerInventoryContainerId="player-items:scope",PlayerWalletContainerId="player-wallet:scope",
            ExpectedPlayerWalletToken=PlayerCommerceContract.WalletSnapshotToken("player","player-wallet:scope",20),
            NetGoldToNpc=3,
            Lines=new List<PersonalBarterLine> {
                new PersonalBarterLine {Direction=PersonalBarterDirection.NpcToPlayer,ItemUid=71,Quantity=1},
                new PersonalBarterLine {Direction=PersonalBarterDirection.PlayerToNpc,ItemUid=81,Quantity=2,
                    ExpectedPlayerInventoryToken=PlayerCommerceContract.InventorySnapshotToken("player","player-items:scope",81,4)}
            }
        };
    }

    private static PersonalBarterState State()
    {
        return new PersonalBarterState {
            NpcId="npc:1",NpcRevision=4,NpcWalletGold=30,PlayerGold=20,
            PlayerInventoryContainerId="player-items:scope",PlayerWalletContainerId="player-wallet:scope",
            PlayerWalletToken=PlayerCommerceContract.WalletSnapshotToken("player","player-wallet:scope",20),
            PlayerConsented=true,NpcConsented=true,NpcAdult=true,NpcEligible=true,NpcWilling=true,PlayerMercantile=0,
            Lines=new List<PersonalBarterLineState> {
                new PersonalBarterLineState {Direction=PersonalBarterDirection.NpcToPlayer,ItemUid=71,SourceQuantity=1,
                    ReceiverCapacity=1,ItemAllowed=true,ItemValued=true},
                new PersonalBarterLineState {Direction=PersonalBarterDirection.PlayerToNpc,ItemUid=81,SourceQuantity=4,
                    ReceiverCapacity=2,PlayerInventoryToken=PlayerCommerceContract.InventorySnapshotToken("player","player-items:scope",81,4),
                    ItemAllowed=true,ItemValued=true}
            }
        };
    }

    private static void Need(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
}
