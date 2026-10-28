using System;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok,string message)
    {checks++;if(!ok)throw new InvalidOperationException(message);}
    private static (WorldStateStore Store,SettlementPopulation Town,Household Home,Resident Reporter,Resident Partner,Resident Child) Family()
    {
        var store=new WorldStateStore(42);
        for(int key=1;key<100;key++)
        {
            var town=store.GetOrCreateFamilies(100+key,new[]{new HomeProfile(key,5,100)});
            var home=town.Households[0];var child=home.Residents.Find(x=>x.AgeGroup==ResidentAgeGroup.Child);
            if(child==null)continue;
            var reporter=home.Residents.Find(x=>x.Id==child.MotherId);
            var partner=home.Residents.Find(x=>x.Id==reporter.PartnerId);
            return(store,town,home,reporter,partner,child);
        }
        throw new Exception("No family fixture.");
    }
    private static void Harm(WorldStateStore store,Resident reporter,int town,int home,string id="harm",string offender="player",int value=-3,double at=100)
    {store.RememberSceneEvent(reporter.Id,id,NpcSceneEventKind.Injury,at,town,home,"I was injured; the player was responsible.",value,reporter.Id,offender);}
    private static void Main()
    {
        var f=Family();Harm(f.Store,f.Reporter,f.Town.SettlementId,f.Home.BuildingKey);
        Check(f.Store.RecallSceneEvents(f.Partner.Id,110)=="","No telepathy before meeting.");
        Check(f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,110)==1,"Actual family meeting shares harm.");
        var report=f.Store.Snapshot().SceneMemories.Find(x=>x.ObserverId==f.Partner.Id);
        Check(report.EventId=="harm" && report.At==100 && report.SubjectId==f.Reporter.Id,"Original event identity/time and affected relative survive.");
        Check(report.Observation.Contains("My partner ") && report.Observation.Contains("told me") && report.Observation.Contains("I did not witness"),"Hearsay is explicit.");
        Check(report.ResponsibleId=="player" && report.Valence<0,"Actual player blame and personal concern reach recall.");
        Check(f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,120)==0,"Same event does not repeat.");
        Check(f.Store.ShareFamilyPlayerExperiences(f.Partner.Id,f.Child.Id,f.Town.SettlementId,f.Home.BuildingKey,120)==0,"Recipient cannot spread secondhand source.");
        Check(f.Store.NpcRelationshipLedgerRevision==0,"Hearsay never duplicates direct relationship punishment.");
        Check(f.Store.Snapshot().AlarmIncidents.Count==0,"Family report never creates a crime.");
        var loaded=new WorldStateStore(1);loaded.Restore(f.Store.Snapshot());
        Check(loaded.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,130)==0,"Current-save restore preserves deduplication.");
        Check(loaded.RecallSceneEvents(f.Partner.Id,130,2000).Contains("told me"),"Current-save recall retains provenance.");
        f=Family();Harm(f.Store,f.Reporter,f.Town.SettlementId,f.Home.BuildingKey);
        Check(f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey+1,110)==0,"Wrong home is not a meeting.");
        Check(f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId+1,f.Home.BuildingKey,110)==0,"Wrong settlement rejected.");
        Check(f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,99)==0,"Future source cannot leak.");
        Check(f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,86501)==0,"Ancient source is not repeated every home frame.");
        f.Partner.IsAlive=false;
        Check(f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,110)==0,"Dead recipient rejected.");
        f=Family();Harm(f.Store,f.Reporter,f.Town.SettlementId,f.Home.BuildingKey,offender:null);
        Check(f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,110)==0,"Unknown offender cannot become player blame.");
        Harm(f.Store,f.Reporter,f.Town.SettlementId,f.Home.BuildingKey,"neutral","player",0);
        Check(f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,110)==0,"Neutral event is not harm.");
        f=Family();Harm(f.Store,f.Reporter,f.Town.SettlementId,f.Home.BuildingKey);
        var p=f.Store.GetOrCreateNpcPersonality(f.Partner.Id,f.Partner.AppearanceSeed,NpcSocialBackground.Commoner);
        var save=f.Store.Snapshot();p=save.NpcPersonalities.Find(x=>x.PersonId==f.Partner.Id);
        p.Warmth=null;p.Patience="patient";p.Curiosity=null;p.Privacy=null;p.Manner=null;p.Outlook=null;p.EmotionalStability=null;p.Ambition=null;p.Integrity=null;p.Courage=null;p.Dutifulness=null;p.Vocabulary=null;p.Eloquence=null;p.StatusAttitude=null;p.Humor=null;p.SelfEsteem=null;p.Sensitivity=null;p.Greed=null;p.Generosity=null;p.Compassion="compassionate";p.Forgiveness="forgiving";p.Humility=null;
        f.Store.Restore(save);f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,110);
        int gentle=f.Store.Snapshot().SceneMemories.Find(x=>x.ObserverId==f.Partner.Id).Valence;
        p.Compassion="callous";p.Forgiveness="vindictive";p.Patience="impatient";f.Store.Restore(save);
        f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,110);
        int harsh=f.Store.Snapshot().SceneMemories.Find(x=>x.ObserverId==f.Partner.Id).Valence;
        Check(harsh<gentle,"Different saved traits produce different concern.");
        f=Family();Harm(f.Store,f.Reporter,f.Town.SettlementId,f.Home.BuildingKey);
        save=f.Store.Snapshot();
        var relation=NpcDirectedRelationship.Create(f.Partner.Id,f.Reporter.Id);
        relation.TrustKnown=true;relation.TrustEvidenceCount=1;relation.TrustConfidence=10;
        relation.ReliabilityTrust=80;save.NpcRelationships.Relationships.Add(relation);
        f.Store.Restore(save);f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,110);
        int trustedFamily=f.Store.Snapshot().SceneMemories.Find(x=>x.ObserverId==f.Partner.Id).Valence;
        relation.ReliabilityTrust=20;relation.Resentment=40;
        f.Store.Restore(save);f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,110);
        int strainedFamily=f.Store.Snapshot().SceneMemories.Find(x=>x.ObserverId==f.Partner.Id).Valence;
        Check(trustedFamily<strainedFamily,"Actual trusted and strained family relations change response.");
        save.NpcRelationships.Relationships.Clear();relation=NpcDirectedRelationship.Create(f.Partner.Id,"player");
        relation.TrustKnown=true;relation.TrustEvidenceCount=1;relation.TrustConfidence=10;
        relation.ReliabilityTrust=80;save.NpcRelationships.Relationships.Add(relation);
        f.Store.Restore(save);f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,110);
        int trustedPlayer=f.Store.Snapshot().SceneMemories.Find(x=>x.ObserverId==f.Partner.Id).Valence;
        relation.ReliabilityTrust=20;relation.Resentment=40;
        f.Store.Restore(save);f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,110);
        int strainedPlayer=f.Store.Snapshot().SceneMemories.Find(x=>x.ObserverId==f.Partner.Id).Valence;
        Check(strainedPlayer<trustedPlayer,"Own player relationship changes response without copying victim hostility.");
        f=Family();
        Check(f.Store.TryBeginNpcObservedSpeechSession(f.Reporter.Id,"family-harassment",out var session),"Issue current-save conversation.");
        Check(f.Store.TryIssueNpcObservedSpeech(session,1,"Unwanted degrading approach.",100,out var ticket),"Issue exact observed speech.");
        object source=new object();var request=NpcSpeechActRequest.Create(source,f.Reporter.Id,"player",null,ticket.ExactText,"");
        Check(NpcSpeechActClassification.TryAccept(request,"DirectHarassment",out var classification),"Semantic harassment accepted.");
        Check(f.Store.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,classification,out var boundary),"Real boundary accepted.");
        Check(f.Store.RecallSceneEvents(f.Partner.Id,110)=="","Accepted boundary stays private before actual meeting.");
        long ledger=f.Store.NpcRelationshipLedgerRevision;
        Check(f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,110)==1,"Accepted harassment shares after meeting.");
        report=f.Store.Snapshot().SceneMemories.Find(x=>x.ObserverId==f.Partner.Id);
        Check(report.EventId==boundary.ReceiptId && report.At==boundary.ObservedAt && report.BuildingKey==-1,"Exact boundary receipt/time survive; unrecorded original place stays unknown.");
        Check(report.Observation.Contains("harassed me") && report.Observation.Contains("told me"),"Spouse knows harassment through partner report.");
        Check(f.Store.ReadNpcConversationBoundary(f.Partner.Id,110)==null && f.Store.NpcRelationshipLedgerRevision==ledger,"Report cannot copy boundary refusal or direct punishment.");
        Check(f.Store.Snapshot().AlarmIncidents.Count==0,"Sharing harassment creates no player crime.");
        f.Child.MotherId=null;f.Child.FatherId=null;
        Check(f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Child.Id,f.Town.SettlementId,f.Home.BuildingKey,110)==0,"Unrelated co-resident receives no family report.");
        f.Reporter.IsAlive=false;
        Check(f.Store.ShareFamilyPlayerExperiences(f.Reporter.Id,f.Partner.Id,f.Town.SettlementId,f.Home.BuildingKey,111)==0,"Dead reporter cannot communicate.");
        Console.WriteLine("PASS "+checks+" family experience checks.");
    }
}
