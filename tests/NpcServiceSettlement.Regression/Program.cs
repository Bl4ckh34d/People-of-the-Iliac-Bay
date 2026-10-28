using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

// Typed native receipts are trusted fixture input. These CPU tests do not prove native player currency movement.
internal static class ServicePaymentStoreRegression
{
    private const string Contract="daily-payment";
    private static int failures;
    private static int Main()
    {
        Run("daily payment publishes NPC wallet and evidence together",PaymentAndRestore);
        Run("wrong proof amount or owned item rejects atomically",WrongProofAndWallet);
        Run("stale inventory epoch and ledger tickets reject",StaleWrites);
        Run("generic ledger cannot publish a payment without wallet",NoGenericBypass);
        Run("failed start reverses NPC wallet once",FailedStartReversal);
        Run("started expiry moves no further currency",TerminationHasNoPayment);
        Run("sibling candidate cannot cross owner",SiblingRejects);
        return failures==0 ? 0 : 1;
    }
    private static void Run(string name,Action body)
    {try{body();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL "+name+": "+e.Message);}}
    private static void PaymentAndRestore()
    {
        var f=Ready();Pay(f);
        Check(f.Store.ReadNpcInventory(f.Npc).WalletGold==127,"exact payment");
        Check(f.Store.ReadNpcInventory(f.Npc).Items[0].NativePayload=="owned","item payload");
        var restored=new WorldStateStore(1);restored.Restore(f.Store.Snapshot());
        Check(Signature(f.Store)==Signature(restored),"current save roundtrip");
        string before=Signature(f.Store);
        Check(!f.Store.TryCommitNpcServiceSettlement(f.Ticket,f.Candidate,f.Credit,new[]{f.Proof},out _),"duplicate payment");
        Check(before==Signature(f.Store),"duplicate payment mutation");
    }
    private static void WrongProofAndWallet()
    {
        var f=Ready();string before=Signature(f.Store);
        Reject(f,null,Array.Empty<NpcServiceTransferReceipt>(),before);
        var altered=NpcServiceTransferReceipt.Create("payment",Contract,NpcServiceTransferKind.Funding,"player",f.Npc,119,0);
        Reject(f,f.Credit,new[]{altered},before);
        Reject(f,f.Credit,new[]{f.Proof,f.Proof},before);
        f.Credit.WalletGold--;Reject(f,f.Credit,new[]{f.Proof},before);f.Credit.WalletGold++;
        f.Credit.Items[0].NativePayload="changed";Reject(f,f.Credit,new[]{f.Proof},before);
    }
    private static void StaleWrites()
    {
        var f=Ready();var old=f.Store.ReadNpcInventory(f.Npc);
        Check(f.Store.CommitNpcInventoryTransaction("unrelated",new[]{new NpcInventoryUpdate {PersonId=f.Npc,
            ExpectedRevision=old.Revision,WalletGold=old.WalletGold,Items=old.Items}}),"fixture revision");
        Reject(f,f.Credit,new[]{f.Proof},Signature(f.Store));
        f=Ready();f.Store.Restore(f.Store.Snapshot());Reject(f,f.Credit,new[]{f.Proof},Signature(f.Store));
        f=Ready();Pay(f);Reject(f,f.Credit,new[]{f.Proof},Signature(f.Store));
    }
    private static void NoGenericBypass()
    {
        var f=Ready();string before=Signature(f.Store);
        Check(!f.Store.TryCommitNpcServiceContracts(f.Ticket,f.Candidate,new[]{f.Proof},Array.Empty<NpcServiceRenewalAuthorization>(),out _),"generic payment bypass");
        Check(before==Signature(f.Store),"generic mutation");
    }
    private static void FailedStartReversal()
    {
        var f=Ready();Pay(f);var ticket=f.Store.CaptureNpcServiceContractWriteTicket();
        var contract=ticket.SourceLedger.Read(Contract);
        var refund=NpcServiceTransferReceipt.Create("rollback",Contract,NpcServiceTransferKind.Refund,f.Npc,"player",120,10);
        Check(contract.TrySettle(contract.Revision,10,NpcServiceEndReason.CancelledBeforeService,f.Npc,null,refund,out var ended),"rollback contract");
        Check(NpcServiceContractLedger.TryReplace(ticket.SourceLedger,ticket.ExpectedLedgerRevision,ended,out var candidate),"rollback ledger");
        var wallet=f.Store.ReadNpcInventory(f.Npc);
        var debit=new NpcInventoryUpdate {PersonId=f.Npc,ExpectedRevision=wallet.Revision,WalletGold=7,Items=wallet.Items};
        string before=Signature(f.Store);
        Check(!f.Store.TryCommitNpcServiceSettlement(ticket,candidate,null,new[]{refund},out _),"rollback needs actual wallet debit");
        Check(before==Signature(f.Store),"failed rollback mutation");
        Check(f.Store.TryCommitNpcServiceSettlement(ticket,candidate,debit,new[]{refund},out var error),error);
        Check(f.Store.ReadNpcInventory(f.Npc).WalletGold==7,"exact rollback");
        Check(!f.Store.TryCommitNpcServiceSettlement(ticket,candidate,debit,new[]{refund},out _),"duplicate rollback");
    }
    private static void TerminationHasNoPayment()
    {
        var f=Ready();Pay(f);var ticket=f.Store.CaptureNpcServiceContractWriteTicket();var contract=ticket.SourceLedger.Read(Contract);
        Check(contract.TryStart(contract.Revision,0,out var started),"start");
        Commit(f.Store,ticket,started);
        ticket=f.Store.CaptureNpcServiceContractWriteTicket();contract=ticket.SourceLedger.Read(Contract);
        Check(contract.TrySettle(contract.Revision,90000,NpcServiceEndReason.NaturalExpiry,f.Npc,null,null,out var ended),"expiry");
        Commit(f.Store,ticket,ended);
        Check(f.Store.ReadNpcInventory(f.Npc).WalletGold==127 && f.Store.Snapshot().NpcServiceEvidence.CompletedTransfers.Count==1,"expiry duplicated payment");
    }
    private static void SiblingRejects()
    {
        var f=Ready();var sibling=new WorldStateStore(2);sibling.Restore(f.Store.Snapshot());
        string before=Signature(sibling);
        Check(!sibling.TryCommitNpcServiceSettlement(f.Ticket,f.Candidate,f.Credit,new[]{f.Proof},out _),"foreign ticket");
        Check(!sibling.TryCommitNpcServiceSettlement(sibling.CaptureNpcServiceContractWriteTicket(),f.Candidate,f.Credit,new[]{f.Proof},out _),"sibling candidate");
        Check(before==Signature(sibling),"sibling mutation");
    }
    private static void Commit(WorldStateStore store,NpcServiceContractWriteTicket ticket,NpcServiceContract contract)
    {
        Check(NpcServiceContractLedger.TryReplace(ticket.SourceLedger,ticket.ExpectedLedgerRevision,contract,out var candidate),"transition");
        Check(store.TryCommitNpcServiceContracts(ticket,candidate,Array.Empty<NpcServiceTransferReceipt>(),Array.Empty<NpcServiceRenewalAuthorization>(),out var error),error);
    }
    private static void Reject(Fixture f,NpcInventoryUpdate credit,IList<NpcServiceTransferReceipt> proof,string before)
    {Check(!f.Store.TryCommitNpcServiceSettlement(f.Ticket,f.Candidate,credit,proof,out _),"invalid payment accepted");Check(Signature(f.Store)==before,"partial mutation");}
    private static void Pay(Fixture f)
    {Check(f.Store.TryCommitNpcServiceSettlement(f.Ticket,f.Candidate,f.Credit,new[]{f.Proof},out var error),error);}
    private static Fixture Ready()
    {
        var f=new Fixture {Store=new WorldStateStore(654)};
        f.Npc=f.Store.GetOrCreateFamilies(3,new[]{new HomeProfile(17,1,60)}).Households[0].Residents[0].Id;
        Check(f.Store.InitializeNpcInventory(f.Npc,7,new[]{new NpcInventoryItem {Uid=70001,NativePayload="owned"}}),"wallet");
        Check(NpcServiceContractTerms.TryCreate(Contract,"player",f.Npc,"home",NpcServiceRole.Fighter,NpcServiceCoverage.Follow,120,false,
            NpcServiceRiskScope.None,Array.Empty<NpcServiceProtectedTarget>(),NpcServiceRestTreatment.RestNotIncluded,
            NpcServiceResupplyResponsibility.SeparatelyNegotiated,Array.Empty<NpcServiceReservedObligation>(),_=>true,_=>true,
            (_,_,_)=>false,(_,_,_,_,_)=>false,out var terms),"terms");
        f.Ticket=f.Store.CaptureNpcServiceContractWriteTicket();
        f.Proof=NpcServiceTransferReceipt.Create("payment",Contract,NpcServiceTransferKind.Funding,"player",f.Npc,120,0);
        Check(NpcServiceContractLedger.TryReserve(f.Ticket.SourceLedger,f.Ticket.ExpectedLedgerRevision,terms,f.Proof,out f.Candidate),"reserve");
        var wallet=f.Store.ReadNpcInventory(f.Npc);
        f.Credit=new NpcInventoryUpdate {PersonId=f.Npc,ExpectedRevision=wallet.Revision,WalletGold=127,Items=wallet.Items};
        return f;
    }
    private static string Signature(WorldStateStore store)=>JsonSerializer.Serialize(store.Snapshot(),new JsonSerializerOptions {IncludeFields=true});
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private sealed class Fixture
    {internal WorldStateStore Store;internal string Npc;internal NpcServiceContractWriteTicket Ticket;internal NpcServiceContractLedger Candidate;internal NpcServiceTransferReceipt Proof;internal NpcInventoryUpdate Credit;}
}
