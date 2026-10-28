using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition,string label)
    {assertions++;if(!condition)throw new InvalidOperationException(label);}
    private static int Summaries(WorldState snapshot)
    {
        FieldInfo field=typeof(WorldState).GetField("SceneMemorySummaries");
        Check(field!=null,"Actual recalled experiences need persisted compact summaries, not only temporary raw recall.");
        return ((System.Collections.ICollection)field.GetValue(snapshot)).Count;
    }
    private static void Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"dfu-scene-summary-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new WorldStateStore(42,root);
            store.RememberSceneEvent("alice","injury:1",NpcSceneEventKind.Injury,100,1,2,"I was injured; the cause was unknown.",-3,"alice");
            store.RememberSceneEvent("alice","injury:2",NpcSceneEventKind.Injury,200,1,2,"I was injured; the cause was unknown.",-3,"alice");
            store.RememberSceneEvent("alice","company:1",NpcSceneEventKind.Company,100,1,2,"Two strangers passed.",0);
            string compact=store.RecallSceneEvents("alice",90000,2000);
            Check(Summaries(store.Snapshot())==1,"Two matching serious observations consolidate; casual neutral detail does not.");
            Check(compact.Contains("2 observations") && compact.IndexOf("I was injured",StringComparison.Ordinal)==compact.LastIndexOf("I was injured",StringComparison.Ordinal),"Repeated observed fact renders once with source count.");
            Check(compact.Contains("100..200") && compact.Contains("place 1/2") && compact.Contains("affected alice") && compact.Contains("responsible unknown") && compact.Contains("-3"),"Summary preserves event times, place, affected identity, unknown cause and signed valuation.");
            Check(!compact.Contains("Two strangers"),"Expired neutral detail does not become durable.");
            var options=new JsonSerializerOptions {IncludeFields=true};
            string json=JsonSerializer.Serialize(store.Snapshot(),options);
            Check(json.Contains("injury:1") && json.Contains("injury:2"),"Persistent summaries retain both exact source IDs.");
            var restored=new WorldStateStore(42,root);restored.Restore(JsonSerializer.Deserialize<WorldState>(json,options));
            // A summary is self-contained evidence, not a pointer to raw rows.
            var summarizedOnly=restored.Snapshot();summarizedOnly.SceneMemories.Clear();restored.Restore(summarizedOnly);
            string old=restored.RecallSceneEvents("alice",90000,2000);
            Check(old.Contains("2 observations") && old.Contains("cause was unknown"),"A real serialized summary remains usable without raw rows.");
            Check(restored.RecallSceneEvents("bob",90000,2000)=="","Other observer never inherits summary.");
            Check(restored.RecallSceneEvents("alice",99,2000)=="","Future source times never leak after clock rewind.");
            Check(!restored.RememberSceneEvent("alice","injury:1",NpcSceneEventKind.Injury,90000,1,2,"Duplicate must not renew.",-3,"alice"),"Remembered source cannot be replayed to renew its lifetime.");
            double expiry=store.Snapshot().SceneMemories.Find(x=>x.EventId=="injury:1").ExpiresAt;
            restored.RecallSceneEvents("alice",expiry-1,2000);
            Check(!restored.RecallSceneEvents("alice",expiry,2000).Contains("100..200"),"Each source keeps its existing randomized retention boundary.");
            Check(restored.RecallSceneEvents("alice",22*86400,2000)=="","Repeated recall never extends existing injury retention.");

            AttributionAndClaims(root);
            CapacityAndBudget(root);
            Validation(root);
            ReappraisedSource(root);
            AppraisalAfterRawEviction(root);
            Console.WriteLine("NPC scene summary regression: "+assertions+" assertions passed.");
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    private static void AttributionAndClaims(string root)
    {
        var store=new WorldStateStore(42,root);
        foreach(string id in new[]{"alice","static:stall"})
        {
            store.RememberSceneEvent(id,"unknown",NpcSceneEventKind.Assault,100,1,2,"I saw an attack.",-3,"victim");
            store.RememberSceneEvent(id,"known",NpcSceneEventKind.Assault,200,1,2,"I saw an attack.",-3,"victim","player");
            store.RememberSceneEvent(id,"other-place",NpcSceneEventKind.Assault,300,1,3,"I saw an attack.",-3,"victim");
            store.RememberSceneEvent(id,"other-subject",NpcSceneEventKind.Assault,400,1,2,"I saw an attack.",-3,"other-victim");
            store.RememberSceneEvent(id,"positive",NpcSceneEventKind.Assault,500,1,2,"I saw an attack.",3,"victim");
            store.RememberSceneEvent(id,"report",NpcSceneEventKind.Help,600,1,2,"The player claimed a murder. The claim remains unverified.",3);
            store.RememberConversation(id,700,"day","inn","I killed a dragon alone.","That is your claim.","model");
            string recall=store.RecallSceneEvents(id,90000,2000);
            Check(store.Snapshot().SceneMemorySummaries.FindAll(x=>x.ObserverId==id).Count==6,"Actor knowledge, place, subject, valuation and hearsay never merge.");
            Check(recall.Contains("responsible unknown") && recall.Contains("responsible player"),"Known and unknown responsible identities stay separate.");
            Check(recall.Contains("claim remains unverified"),"Observed receipt of a claim never confirms the claimed event.");
            Check(!recall.Contains("dragon"),"Player dialogue never becomes an observed scene summary.");
        }
        var town=store.GetOrCreate(1,new[]{7,8},1);
        var victim=town.Households[0].Residents[0];string witness=town.Households[1].Residents[0].Id;
        var incident=store.RecordViolence(1,victim.Id,100);store.LocateViolence(incident.Id,-1,0,0,0);
        Population.MarkDead(town,victim.Id);store.ConfirmCorpse(incident.Id,100,-1,0,0,0);store.Witness(incident.Id,witness,100);
        store.RecallSceneEvents(witness,90000,2000);store.MarkPlayerMurder(incident.Id);
        string refined=store.RecallSceneEvents(witness,90000,2000);
        Check(refined.Contains("responsible player") && refined.Contains("The player killed them."),"Later native attribution reprojects compacted source without inventing a new event.");
        var summaries=store.Snapshot().SceneMemorySummaries.FindAll(x=>x.ObserverId==witness && x.Kind==NpcSceneEventKind.Murder);
        Check(summaries.Count==1 && summaries[0].Sources.Count==1 && summaries[0].Sources[0].At==100,"Attribution refinement preserves original source and event time.");

        var late=new WorldStateStore(43,root);var lateTown=late.GetOrCreate(2,new[]{7,8},1);
        var dead=lateTown.Households[0].Residents[0];string observer=lateTown.Households[1].Residents[0].Id;
        var lateIncident=late.RecordViolence(2,dead.Id,100);late.LocateViolence(lateIncident.Id,-1,0,0,0);
        Population.MarkDead(lateTown,dead.Id);late.ConfirmCorpse(lateIncident.Id,100,-1,0,0,0);late.Witness(lateIncident.Id,observer,100);
        late.RecallSceneEvents(observer,90000,2000);
        for(int i=0;i<40;i++)late.RememberSceneEvent(observer,"new-murder:"+i,NpcSceneEventKind.Murder,200+i,2,7,"Another witnessed killing.");
        Check(!late.Snapshot().SceneMemories.Exists(x=>x.EventId==lateIncident.Id+":"+lateIncident.GenerationSerial && x.Kind==NpcSceneEventKind.Murder),"Controlled flood evicts the raw death copy before later native attribution.");
        late.MarkPlayerMurder(lateIncident.Id);late.RecallSceneEvents(observer,90000,2000);
        Check(late.Snapshot().SceneMemorySummaries.Exists(x=>x.ObserverId==observer && x.Kind==NpcSceneEventKind.Murder &&
            x.SubjectId==dead.Id && x.ResponsibleId=="player"),"Native attribution also refines a source surviving only in a summary.");
    }

    private static void CapacityAndBudget(string root)
    {
        var store=new WorldStateStore(42,root);
        for(int i=0;i<32;i++)store.RememberSceneEvent("same","source:"+i,NpcSceneEventKind.Injury,100+i,1,2,"Repeated injury.",-3,"same");
        store.RecallSceneEvents("same",90000,2000);
        var group=store.Snapshot().SceneMemorySummaries.Find(x=>x.ObserverId=="same");
        Check(group.Sources.Count==8 && group.Sources.Exists(x=>x.EventId=="source:31"),"A repeated fact retains bounded most recent sources including the last raw record.");
        string before=JsonSerializer.Serialize(store.Snapshot(),new JsonSerializerOptions {IncludeFields=true});
        store.RecallSceneEvents("same",90000,2000);
        Check(before==JsonSerializer.Serialize(store.Snapshot(),new JsonSerializerOptions {IncludeFields=true}),"Repeated recall is idempotent without source or lifetime renewal.");
        for(int i=0;i<32;i++)store.RememberSceneEvent("alice","different:"+i,NpcSceneEventKind.Injury,100+i,1,2,"Different actual injury "+i+".",-3,"alice");
        store.RecallSceneEvents("alice",90000,2000);
        Check(store.Snapshot().SceneMemorySummaries.FindAll(x=>x.ObserverId=="alice").Count==16,"Distinct long-term facts obey the per-observer group bound.");
        store.RememberSceneEvent("alice","today-trade",NpcSceneEventKind.Trade,89999,1,2,"Today's player trade was cancelled. Nothing changed hands.",0,"player");
        store.RememberConversation("alice",89998,"day","inn","My recent silver ring question.","Recent ring answer.","model");
        NpcMemoryReadSnapshot read;Check(store.TryCaptureNpcMemoryRead("alice","silver ring",90000,2000,out read),"Actual memory tool captures compact evidence.");
        Check(read.Text.Length<=2000 && read.Text.Contains("Today's player trade") && read.Text.Contains("silver ring"),"Compact trauma cannot crowd out same-day trade or queried dialogue.");
        Check(store.IsNpcMemoryReadCurrent(read,"alice",90000),"Summary-aware receipt validates without another consolidation.");
        string saved=JsonSerializer.Serialize(store.Snapshot(),new JsonSerializerOptions {IncludeFields=true});
        store.IsNpcMemoryReadCurrent(read,"alice",90000);
        Check(saved==JsonSerializer.Serialize(store.Snapshot(),new JsonSerializerOptions {IncludeFields=true}),"Final receipt validation never mutates persistent summaries.");
    }

    private static void Validation(string root)
    {
        var store=new WorldStateStore(42,root);
        store.RememberSceneEvent("alice","source",NpcSceneEventKind.Injury,100,1,2,"Injured.",-3,"alice");
        store.RecallSceneEvents("alice",90000,2000);
        var snapshot=store.Snapshot();snapshot.SceneMemorySummaries[0].Sources[0].At=double.NaN;
        bool rejected=false;try{store.Restore(snapshot);}catch(ArgumentException){rejected=true;}
        Check(rejected,"Non-finite summary source times fail save validation.");
        snapshot=store.Snapshot();snapshot.SceneMemorySummaries[0].Sources.Add(snapshot.SceneMemorySummaries[0].Sources[0]);
        rejected=false;try{store.Restore(snapshot);}catch(ArgumentException){rejected=true;}
        Check(rejected,"Duplicated source identity fails save validation.");
        snapshot=store.Snapshot();snapshot.Version=82;
        rejected=false;try{store.Restore(snapshot);}catch(ArgumentException){rejected=true;}
        Check(rejected,"Old save schema cannot smuggle future summary fields.");
    }

    private static void ReappraisedSource(string root)
    {
        var store=new WorldStateStore(42,root);
        store.RememberSceneEvent("alice","gift:large",NpcSceneEventKind.Gift,100,1,2,"I received a gift.",5,"alice");
        store.RecallSceneEvents("alice",90000,2000);
        Check(store.Snapshot().SceneMemorySummaries.Count==1,"Exceptional gift initially qualifies as relevant long-term evidence.");
        // Historical save fixture: accepted native appraisal updates its raw
        // source; the derived summary still carries the preceding valuation.
        var appraised=store.Snapshot();appraised.SceneMemories[0].Valence=0;store.Restore(appraised);
        string current=store.RecallSceneEvents("alice",90000,2000);
        Check(!current.Contains("my evaluation +5") && store.Snapshot().SceneMemorySummaries.Count==0,
            "A source reappraised below relevance must retire its obsolete compact valuation.");
    }

    private static void AppraisalAfterRawEviction(string root)
    {
        var store=new WorldStateStore(42,root);string person=store.GetOrCreate(10,new[]{20},1).Households[0].Residents[0].Id;
        store.RememberSceneEvent(person,"outcome:gift",NpcSceneEventKind.Gift,100,10,20,"I received a gift.",5,person);
        store.RecallSceneEvents(person,90000,2000);
        var saved=store.Snapshot();saved.SceneMemories.Clear();store.Restore(saved);
        var evidence=NpcExperienceEvent.FromNativeOutcome(person,person,"receipt:gift","episode:gift","outcome:gift",
            NpcExperienceEventKind.GiftReceived,100,NpcResponsibleActorAttribution.Unknown);
        HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store,evidence);
        store.GetOrCreateNpcPersonality(person,42,NpcSocialBackground.Commoner);
        NpcExperienceAppraisalTicket ticket;Check(store.TryCaptureNpcExperienceAppraisal(person,90000,out ticket),"Retained-source appraisal captures its existing event.");
        var r=ticket.Request;
        NpcMemoryReadSnapshot before;Check(store.TryCaptureNpcMemoryRead(person,"gift",90000,2000,out before),"Capture summary evidence before the pending appraisal commits.");
        string response=JsonSerializer.Serialize(new {
            schemaVersion=NpcExperienceAppraisalContract.SchemaVersion,policyVersion=NpcExperienceAppraisalContract.PolicyVersion,
            saveBranchId=r.SaveBranchId,observerId=r.ObserverId,subjectPersonId=r.SubjectPersonId,
            evidenceDeduplicationKey=r.EvidenceDeduplicationKey,canonicalEvidenceId=r.CanonicalEvidenceId,
            relationshipTargetId=r.RelationshipTargetId??"",expectedMoodRevision=r.ExpectedMoodRevision,
            expectedAffinityRevision=r.ExpectedAffinityRevision,expectedRelationshipRevision=r.ExpectedRelationshipRevision,
            moodDelta=0,affinityDelta=0,trustDelta=0,resentmentDelta=0,fearDelta=0,speechActKind="NotApplicable",
            speechTargetPersonId="",conversationOption="NoAction"});
        Check(store.TryCommitNpcExperienceAppraisal(ticket,response),"Real accepted appraisal commits neutral value after raw eviction.");
        Check(!store.IsNpcMemoryReadCurrent(before,person,90000),"A committed appraisal invalidates an older summary receipt before any new consolidation.");
        store.RecallSceneEvents(person,90000,2000);
        Check(store.Snapshot().SceneMemorySummaries.Count==0,"Accepted appraisal cannot leave stale high-valence evidence when only its summary survives.");
    }
}
