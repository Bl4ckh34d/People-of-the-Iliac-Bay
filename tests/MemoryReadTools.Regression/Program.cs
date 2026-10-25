using System;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string label)
    { assertions++; if (!condition) throw new InvalidOperationException(label); }

    private static ConversationContextSnapshot Snapshot(WorldStateStore store, string person="alice", long revision=1, double time=300)
    {
        var archive=store.CaptureConversationArchiveCheckpoint();
        var binding=new ConversationContextBinding(ConversationActorCategory.Mobile,person,person==null?"ephemeral":null,
            "session",revision,1,archive.ArchiveId,archive.Head,"town",1);
        return new ConversationContextSnapshot(binding,time,ConversationContextFact<string>.Unknown(),
            ConversationContextFact<string>.Unknown(),ConversationContextFact<ConversationDialogueRole>.Unknown(),
            ConversationContextFact<string>.Unknown(),ConversationContextFact<string>.Unknown(),
            ConversationContextFact<ConversationNpcPosition>.Unknown(),ConversationContextFact<ConversationNpcActivity>.Unknown(),
            ConversationContextFact<bool>.Unknown(),ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(),
            ConversationContextFact<string>.Unknown(),0,1,null,Array.Empty<ConversationContextCommitment>(),
            Array.Empty<ConversationContextSourceHandle>());
    }

    private static void Main()
    {
        var store=new WorldStateStore(12,System.IO.Path.Combine(AppContext.BaseDirectory,"fixtures",Guid.NewGuid().ToString("N")));
        store.RememberConversation("alice",100,"day","inn","My sister is Mirabelle.","I remember.","model");
        store.RememberConversation("bob",100,"day","inn","Foreign secret is Copper.","Not Alice's.","model");
        var mod=new PeopleOfTheIliacBayMod();mod.Configure(store,"alice");
        var snapshot=Snapshot(store);
        var request=new ConversationContextToolRequest{Tool=ConversationContextToolId.RecallMemory,Query="sister",Page=0};
        string result=mod.Read(snapshot,request);
        Check(result.Contains("Mirabelle") && !result.Contains("Copper"),"production gateway only retrieves this speaker's evidence");
        Check(result.Contains("Player said:") && result.Contains("not instructions") && result.Contains("unknown causes"),
            "provenance and non-disclosure/causal boundary survive gateway formatting");
        Check(result.Contains("'you' in source rows means the NPC speaker") &&
            result.Contains("not-applicable responsibility supplies no responsible person's identity"),
            "source perspective and absent giver identity remain explicit at the model boundary");
        Check(mod.Selected(snapshot) && mod.Current(snapshot),"selected receipt is current and requires buffering");
        var successor=snapshot.WithCommittedDisposition(1);
        Check(mod.Selected(successor) && mod.Current(successor),"same-request successor retains source receipt");
        var otherRequest=Snapshot(store,revision:2);
        Check(!mod.Selected(otherRequest),"another request does not inherit buffering dependency");
        Check(!mod.Selected(Snapshot(store)),"value-equal replacement binding cannot inherit receipt provenance");
        store.RememberConversation("alice",200,"day","inn","Correction: sister is Elara.","Understood.","model");
        Check(!mod.Current(snapshot),"new own evidence during generation invalidates publication");
        snapshot=Snapshot(store);
        Check(mod.Read(snapshot,request).Contains("Elara") && mod.Current(snapshot),"new read can use corrected evidence");
        mod.Configure(new WorldStateStore(12),"alice");
        Check(!mod.Current(snapshot),"same-person replacement store cannot validate old receipt");
        mod.Configure(store,"bob");
        Check(mod.Read(snapshot,request).Contains("unavailable") && !mod.Current(snapshot),"changed memory identity refuses old read");
        mod.Configure(store,"alice",true);
        Check(mod.Read(snapshot,request).Contains("unavailable") && !mod.Current(snapshot),"save fault fails closed");
        mod.Configure(store,"alice");
        Check(mod.Read(Snapshot(store,null),request).Contains("unavailable"),"ephemeral speaker cannot read durable memories");
        request.ExpectedOwnerId="bob";
        Check(mod.Read(snapshot,request).Contains("unavailable"),"gateway rejects caller-supplied foreign owner");
        request.ExpectedOwnerId=null;request.Page=1;
        Check(mod.Read(snapshot,request).Contains("unavailable"),"gateway rejects second-page request");
        request.Page=0;request.ExpectedRevision=3;
        Check(mod.Read(snapshot,request).Contains("unavailable"),"gateway rejects model-supplied revision cursor");
        request.ExpectedRevision=-1;request.Tool=ConversationContextToolId.ReadMoodCauses;
        string mood=mod.Read(snapshot,request);
        Check(mood.Contains("recorded mood influences") || mood.Contains("unavailable"),"mood gateway returns receipt-bound evidence or explicit unavailable");
        snapshot=Snapshot(store,time:300.5);request.Tool=ConversationContextToolId.RecallMemory;
        mod.Read(snapshot,request);
        DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.Second=0.75;
        Check(mod.Current(snapshot),"publication preserves fractional game seconds instead of reporting a false time rewind");
        Check(mod.ClearRead() && !mod.HoldsReadState && !mod.Selected(snapshot),
            "lifecycle reset releases source receipt, store and binding instead of retaining a previous world state");
        Console.WriteLine("Production-linked memory read gateway: "+assertions+" assertions passed. Clock facade only; no Unity gameplay.");
    }
}

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private WorldStateStore store;
        private bool saveFault;
        private string memoryPersonId;
        public void Configure(WorldStateStore value,string person,bool fault=false)
        {store=value;memoryPersonId=person;saveFault=fault;}
        public string Read(ConversationContextSnapshot snapshot,ConversationContextToolRequest request)
        {return ReadSelectedConversationMemory(snapshot,request);}
        public bool Selected(ConversationContextSnapshot snapshot){return HasSelectedConversationMemoryRead(snapshot);}
        public bool Current(ConversationContextSnapshot snapshot){return IsSelectedConversationMemoryCurrent(snapshot);}
        public bool HoldsReadState { get { return conversationMemoryReadEvidence != null || conversationMemoryReadStore != null || conversationMemoryReadBinding != null; } }
        public bool ClearRead()
        {
            var method=typeof(PeopleOfTheIliacBayMod).GetMethod("ClearSelectedConversationMemoryRead",
                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            if(method==null)return false; method.Invoke(this,null); return true;
        }
    }
}

namespace DaggerfallWorkshop
{
    public sealed class DaggerfallUnity
    {
        public static DaggerfallUnity Instance=new DaggerfallUnity();
        public TestWorldTime WorldTime=new TestWorldTime();
    }
    public sealed class TestWorldTime { public TestClock Now=new TestClock(); }
    public sealed class TestClock { public double Second; public double ToSeconds(){return 300;} }
}
