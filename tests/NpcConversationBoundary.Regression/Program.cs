using System;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int failures,executed;
    private static int Main()
    {
        Run("direct insult closes but permits hostile reopen", InsultBoundary);
        Run("direct threat persists one pending physical report", ThreatBoundary);
        Run("report expiry does not force a threatened NPC to resume communication", ExpiredReport);
        Run("spoofed and stale sources cannot commit a boundary", RejectSpoofAndStale);
        Run("restore validates receipt source and migrates legacy empty", RestoreValidation);
        Run("refusal rejects ordinary questions without reporting and repeated insults cause a finite cooldown", RenewedSpeechEscalates);
        Run("sincere repair clears refusal without erasing offense", RepairPreservesOffense);
        Run("verified nobles and guards do not acquire player fear", ProtectedRolesKeepFearPolicy);
        Run("personality changes insult cooldown without changing the offense evidence", PersonalityCooldown);
        Run("renewed misconduct and an apology work after an expired cooldown", ExpiredCooldown);
        Run("schema62 boundaries survive migration without admitting schema63 fields", LegacyBoundary);
        Run("legacy nonthreat complaints are abandoned without erasing their evidence", LegacyNonThreatReport);
        Run("only a delivered direct threat can carry a persistent verbal court charge", VerbalCourtCharge);
        Console.WriteLine((executed-failures)+"/"+executed+" passed");
        return failures==0?0:1;
    }

    private static void ExpiredCooldown()
    {
        var store=Create(out string person);
        var ticket=Issue(store,person,"You are a fool.",10);object source=new object();
        Check(store.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,
            Classify(source,person,ticket.ExactText,"DirectInsult"),out var first),"first boundary");
        Check(Accept(store,ticket),"first accepted");
        double now=first.RefuseUntil+1;
        ticket=Issue(store,person,"You are a fool.",now);source=new object();
        Check(store.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,
            Classify(source,person,ticket.ExactText,"DirectInsult"),out var repeated),"renewed insult remembers unrepaired offense after expiry");
        Check(repeated.ParentBoundaryOrdinal==first.Ordinal && repeated.RefuseUntil>=now+2*86400,"repeat duration");
        Check(Accept(store,ticket),"repeat accepted");
        now=repeated.RefuseUntil+1;
        ticket=Issue(store,person,"I am sorry for insulting you.",now);source=new object();
        Check(store.TryRepairNpcConversationBoundary(ticket,ticket.Receipt,source,
            Classify(source,person,ticket.ExactText,"RepairAttempt")),"contextual apology after expiry");
        Check(Accept(store,ticket),"repair accepted");
        var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
        Check(!restored.HasUnrepairedNpcConversationBoundary(person,now),"repair persists");
        Check(restored.ReadNpcRelationship(person,"player",now).ProjectedResentment>0,"repair retains resentment");
    }

    private static void LegacyBoundary()
    {
        var store=Create(out string person);
        var ticket=Issue(store,person,"You are a fool.",10);object source=new object();
        Check(store.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,
            Classify(source,person,ticket.ExactText,"DirectInsult"),out _),"source");
        Check(Accept(store,ticket),"accepted");
        var legacy=store.Snapshot();legacy.Version=62;
        Throws(()=>new WorldStateStore(0).Restore(legacy));
        var boundary=legacy.ConversationAttitudes[0].Boundary;
        boundary.PolicyVersion=0;boundary.RefusalSeconds=0;boundary.RefuseUntil=1810;
        var restored=new WorldStateStore(0);restored.Restore(legacy);
        Check(restored.ReadNpcConversationBoundary(person,10).PolicyVersion==0,"legacy receipt preserved");
        Check(restored.ShouldEnforceNpcConversationBoundary(person,10),"legacy first refusal retained");
        Check(!restored.ShouldEnforceNpcConversationBoundary(person,1810),"legacy expiry honored without renewed punishment");
    }

    private static void LegacyNonThreatReport()
    {
        var store=Create(out string person);
        var ticket=Issue(store,person,"You are a disgrace.",10);object source=new object();
        Check(store.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,
            Classify(source,person,ticket.ExactText,"SevereAbuse"),out _),"offense");
        Check(Accept(store,ticket),"accepted");
        var legacy=store.Snapshot();legacy.Version=62;
        var boundary=legacy.ConversationAttitudes[0].Boundary;
        boundary.PolicyVersion=0;boundary.RefusalSeconds=0;boundary.RefuseUntil=1810;
        boundary.Action=ConversationSpeechBoundaryAction.EndAndReport;
        boundary.ReportStatus=NpcConversationBoundaryReportStatus.Started;boundary.RefusesFurtherCommunication=false;
        var restored=new WorldStateStore(0);restored.Restore(legacy);
        Check(restored.CanTalkToNpcAfterBoundary(person,11),"old nonthreat report cannot block contact");
        Equal(0,restored.ReadStartedNpcConversationBoundaryReports(11).Count);
        Check(restored.TryAbandonExpiredNpcConversationBoundaryReport(person,11),"unsupported legacy report abandons immediately");
        Check(!restored.TryMarkNpcConversationBoundaryReport(person,boundary.ReceiptId,NpcConversationBoundaryReportStatus.Started),"cannot restart");
        restored.Restore(restored.Snapshot());
        Check(restored.ReadNpcRelationship(person,"player",11).ProjectedResentment>=65,"old offense retained");
    }

    private static void PersonalityCooldown()
    {
        var forgiving=new NpcPersonalityProfile{PersonId="npc:person",Version=3,Forgiveness="forgiving",Sensitivity="thick-skinned",Patience="patient"};
        var sensitive=new NpcPersonalityProfile{PersonId="npc:person",Version=3,Forgiveness="vindictive",Sensitivity="highly sensitive",Patience="impatient"};
        double shortWait=ConversationSpeechBoundaryPolicy.RefusalSeconds(forgiving,true);
        double longWait=ConversationSpeechBoundaryPolicy.RefusalSeconds(sensitive,true);
        Check(shortWait>=2*86400 && longWait<=7*86400 && shortWait<longWait,"forgiveness and sensitivity govern bounded days");
        Check(ConversationSpeechBoundaryPolicy.RefusalSeconds(forgiving,false)<shortWait,"first offense allows earlier careful approach");
    }

    private static void InsultBoundary()
    {
        var store=Create(out string person);
        var ticket=Issue(store,person,"You are a fool.",10);
        object source=new object();
        var kind=Classify(source,person,ticket.ExactText,"DirectInsult");
        Check(store.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,kind,out var boundary),"insult commits");
        Equal(ConversationSpeechBoundaryAction.EndConversation,boundary.Action);
        Equal(NpcConversationBoundaryReportStatus.None,boundary.ReportStatus);
        Check(boundary.RefusesFurtherCommunication,"minor boundary leaves standing refusal");
        Check(store.ShouldEnforceNpcConversationBoundary(person,10),"standing refusal is active");
        Check(store.ShouldEnforceNpcConversationBoundary(person,2000),
            "standing refusal does not silently expire before repair");
        Check(store.CaptureConversationMood(person,10).Value<=-40,"offense anchors angry mood immediately");
        Check(store.CaptureConversationMood(person,3600).Value<=-20,"anger survives real elapsed game hours");
        Check(store.ReadNpcRelationship(person,NpcDirectedRelationship.PlayerTargetId,10).ProjectedResentment>=65,
            "one accepted boundary event anchors high resentment");
        Check(store.ReadNpcRelationship(person,NpcDirectedRelationship.PlayerTargetId,86410).ProjectedResentment>=40,
            "resentment remains high after a game day");
        Check(store.CanTalkToNpcAfterBoundary(person,10),"minor boundary allows immediate hostile reopen");
        Check(boundary.RefuseUntil>2000 && boundary.RefuseUntil<=86410,"first refusal remains recent for hours, not forever");
        store.SetConversationBaseline(person,20,10,72);
        var attitude=store.Snapshot().ConversationAttitudes.Find(x=>x.PersonId==person);
        int before=attitude.Offset;
        int applied=store.SetConversationBoundaryDispositionAtMost(person,20,-50,10);
        Equal(-70,store.ConversationDisposition(person));
        Equal(-70-before,applied);
        Equal(0,store.SetConversationBoundaryDispositionAtMost(person,20,-50,10));
        Equal(0,store.SetConversationBoundaryDispositionAtMost(person,20,-100,11)-(-50));
        Equal(-100,store.ConversationDisposition(person)+20);
        Check(store.SetConversationBoundaryDispositionAtMost(person,20,-50,12)<=0,
            "lesser boundary cannot soften a prior terminal disposition");
        Equal(72,store.Snapshot().ConversationAttitudes.Find(x=>x.PersonId==person).NativeFactionId);
        Check(!store.TryMarkNpcConversationBoundaryReport(person,boundary.ReceiptId,
            NpcConversationBoundaryReportStatus.Started),"nonreport cannot start");
        Check(Accept(store,ticket),"speech accepted after response append");
        var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
        Equal(boundary.ReceiptId,restored.ReadNpcConversationBoundary(person,10).ReceiptId);
        Check(restored.CaptureConversationMood(person,10).Value<=-40,"anger survives restore");
        Check(restored.ReadNpcRelationship(person,NpcDirectedRelationship.PlayerTargetId,10).ProjectedResentment>=65,
            "resentment survives restore");
        Check(!restored.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,kind,out _),"old ticket cannot replay");
    }

    private static void ExpiredReport()
    {
        var store=Create(out string person);
        var ticket=Issue(store,person,"I will hurt you.",10);
        object source=new object();
        Check(store.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,
            Classify(source,person,ticket.ExactText,"DirectThreat"),out var boundary),"boundary commits");
        Check(Accept(store,ticket),"threat is accepted");
        Check(store.TryMarkNpcConversationBoundaryReport(person,boundary.ReceiptId,
            NpcConversationBoundaryReportStatus.Started),"report setup starts");
        Check(!store.TryAbandonExpiredNpcConversationBoundaryReport(person,1089),"cannot expire early");
        var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
        Check(restored.TryAbandonExpiredNpcConversationBoundaryReport(person,1090),"orphaned report expires");
        Check(!restored.CanTalkToNpcAfterBoundary(person,1090),"threat refusal survives report expiry");
        Check(!restored.TryMarkNpcConversationBoundaryReport(person,boundary.ReceiptId,
            NpcConversationBoundaryReportStatus.Delivered),"abandoned report cannot claim delivery");
        Check(restored.ReadNpcRelationship(person,"player",1090).ProjectedResentment>=60,
            "report expiry does not erase resentment");
    }

    private static void ThreatBoundary()
    {
        var store=Create(out string person);
        var ticket=Issue(store,person,"If you refuse, I will hurt you.",10);
        object source=new object();
        var kind=Classify(source,person,ticket.ExactText,"DirectThreat");
        Check(store.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,kind,out var pending),"threat commits");
        Equal(ConversationSpeechBoundaryAction.EndAndReport,pending.Action);
        Equal(NpcConversationBoundaryReportStatus.Pending,pending.ReportStatus);
        Check(store.ReadNpcRelationship(person,NpcDirectedRelationship.PlayerTargetId,10)
            .ProjectedInterpersonalFear>=65,"threat gives an ordinary civilian durable fear");
        Check(store.ReadNpcRelationship(person,NpcDirectedRelationship.PlayerTargetId,86410)
            .ProjectedInterpersonalFear>=50,"fear remains high after a game day");
        Check(Accept(store,ticket),"threat speech accepted");
        Check(store.TryMarkNpcConversationBoundaryReport(person,pending.ReceiptId,
            NpcConversationBoundaryReportStatus.Started),"report starts once");
        Check(store.TryMarkNpcConversationBoundaryReport(person,pending.ReceiptId,
            NpcConversationBoundaryReportStatus.Started),"same report state is idempotent");
        Check(store.TryStartReceiptDialogueComplaint(new DialogueComplaintRecord{PersonId=person,SourceReceiptId=pending.ReceiptId,
            SettlementId=11,StartedAt=10,LastUpdatedAt=10,ExpiresAt=1090,NextWaypoint=1,
            Route=new System.Collections.Generic.List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=1}}}),
            "source-bound threat complaint starts");
        Check(!store.TryRetireUnsupportedDialogueComplaint(person,11),"live exact threat remains reportable");
        Equal(1,store.ReadStartedNpcConversationBoundaryReports(11).Count);
        Check(!store.TryMarkNpcConversationBoundaryReport(person,"wrong-receipt",
            NpcConversationBoundaryReportStatus.Delivered),"wrong receipt cannot deliver");
        var next=Issue(store,person,"You are a fool.",11);
        object nextSource=new object();
        Check(!store.TryCommitNpcConversationBoundary(next,next.Receipt,nextSource,
            Classify(nextSource,person,next.ExactText,"DirectInsult"),out _),
            "new speech cannot erase a still-pending physical report");
        var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
        Check(restored.ReadNpcRelationship(person,NpcDirectedRelationship.PlayerTargetId,10)
            .ProjectedInterpersonalFear>=65,"threat fear survives restore");
        Check(!restored.CanTalkToNpcAfterBoundary(person,2000),"started report keeps witness unavailable");
        Check(restored.TryMarkNpcConversationBoundaryReport(person,pending.ReceiptId,
            NpcConversationBoundaryReportStatus.Delivered),"arrival delivers report");
        Check(!restored.TryMarkNpcConversationBoundaryReport(person,pending.ReceiptId,
            NpcConversationBoundaryReportStatus.Started),"delivered report cannot regress");
        Check(!restored.CanTalkToNpcAfterBoundary(person,2000),"threatened NPC still refuses after report delivery");
        string abandonedPerson;var abandonedStore=Create(out abandonedPerson);
        var abandonedTicket=Issue(abandonedStore,abandonedPerson,"I will hurt you.",10);
        object abandonedSource=new object();
        Check(abandonedStore.TryCommitNpcConversationBoundary(abandonedTicket,abandonedTicket.Receipt,
            abandonedSource,Classify(abandonedSource,abandonedPerson,abandonedTicket.ExactText,"DirectThreat"),
            out var abandoned),"second pending report commits");
        Check(Accept(abandonedStore,abandonedTicket),"second report speech accepted");
        Check(abandonedStore.TryMarkNpcConversationBoundaryReport(abandonedPerson,abandoned.ReceiptId,
            NpcConversationBoundaryReportStatus.Abandoned),"no route may abandon report");
        Check(!abandonedStore.CanTalkToNpcAfterBoundary(abandonedPerson,100),"abandoned threat report does not restore communication");
        Check(!abandonedStore.CanTalkToNpcAfterBoundary(abandonedPerson,2000),"threat refusal remains after abandonment");
        Check(!abandonedStore.TryMarkNpcConversationBoundaryReport(abandonedPerson,abandoned.ReceiptId,
            NpcConversationBoundaryReportStatus.Started),"abandoned report cannot restart");
    }

    private static void RejectSpoofAndStale()
    {
        var store=Create(out string person);
        var first=Issue(store,person,"You are a fool.",10);
        object source=new object();
        var wrong=Classify(new object(),person,first.ExactText,"DirectInsult");
        Check(!store.TryCommitNpcConversationBoundary(first,first.Receipt,source,wrong,out _),"foreign classifier source rejected");
        var wrongText=Classify(source,person,"Different insult.","DirectInsult");
        Check(!store.TryCommitNpcConversationBoundary(first,first.Receipt,source,wrongText,out _),"classifier text mismatch rejected");
        var second=Issue(store,person,"If you refuse, I will hurt you.",11);
        var stale=Classify(source,person,first.ExactText,"DirectInsult");
        Check(!store.TryCommitNpcConversationBoundary(first,first.Receipt,source,stale,out _),"superseded ticket rejected");
        Check(store.ReadNpcConversationBoundary(person,11)==null,"no rejected write");
        var valid=Classify(source,person,second.ExactText,"DirectThreat");
        Check(store.TryCommitNpcConversationBoundary(second,second.Receipt,source,valid,out var boundary),"current threat commits");
        long moodRevision=store.CaptureConversationMood(person,11).Revision;
        Check(!store.TryCommitNpcConversationBoundary(second,second.Receipt,source,valid,out _),"duplicate does not mutate");
        Equal(moodRevision,store.CaptureConversationMood(person,11).Revision);
        Check(Accept(store,second),"current speech accepted");
    }

    private static void RestoreValidation()
    {
        var store=Create(out string person);
        var ticket=Issue(store,person,"You are a fool.",10);
        object source=new object();
        Check(store.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,
            Classify(source,person,ticket.ExactText,"DirectInsult"),out _),"boundary commits");
        Check(Accept(store,ticket),"speech accepted");
        var altered=store.Snapshot();
        altered.ConversationAttitudes[0].Boundary.ReceiptId="forged";
        Throws(()=>new WorldStateStore(0).Restore(altered));
        altered=store.Snapshot();
        altered.ConversationAttitudes[0].Boundary.Ordinal++;
        Throws(()=>new WorldStateStore(0).Restore(altered));
        altered=store.Snapshot();
        altered.ConversationAttitudes[0].Boundary.Action=ConversationSpeechBoundaryAction.EndAndReport;
        Throws(()=>new WorldStateStore(0).Restore(altered));
        altered=store.Snapshot();
        altered.ConversationAttitudes[0].Boundary.RefuseUntil++;
        Throws(()=>new WorldStateStore(0).Restore(altered));
        altered=store.Snapshot();
        altered.NpcRelationships.AcceptedOutcomes[0].ConversationBoundaryOrdinal++;
        Throws(()=>new WorldStateStore(0).Restore(altered));
        var legacy=store.Snapshot();legacy.Version=59;
        Throws(()=>new WorldStateStore(0).Restore(legacy));
        legacy.ConversationAttitudes[0].Boundary=null;
        var migrated=new WorldStateStore(0);migrated.Restore(legacy);
        Check(migrated.ReadNpcConversationBoundary(person,10)==null,"legacy has no invented boundary");
    }

    private static void RenewedSpeechEscalates()
    {
        var store=Create(out string person);
        var insult=Issue(store,person,"You are a fool.",10);
        object firstSource=new object();
        Check(store.TryCommitNpcConversationBoundary(insult,insult.Receipt,firstSource,
            Classify(firstSource,person,insult.ExactText,"DirectInsult"),out var first),"first refusal");
        Check(Accept(store,insult),"first speech accepted");
        var ordinary=Issue(store,person,"Could you tell me where an armor shop is?",11);
        object ordinarySource=new object();
        var ordinaryKind=Classify(ordinarySource,person,ordinary.ExactText,"Other",true);
        Equal(ConversationSpeechBoundaryAction.EndConversation,ConversationSpeechBoundaryPolicy.Decide(ordinarySource,ordinaryKind));
        Check(!store.TryCommitNpcConversationBoundary(ordinary,ordinary.Receipt,ordinarySource,ordinaryKind,out _),
            "ordinary follow-up creates no punitive boundary");
        Equal(first.ReceiptId,store.ReadNpcConversationBoundary(person,11).ReceiptId);
        Equal(NpcConversationBoundaryReportStatus.None,store.ReadNpcConversationBoundary(person,11).ReportStatus);
        Check(store.CanTalkToNpcAfterBoundary(person,11),"ordinary contact does not create a physical report");
        Check(Accept(store,ordinary),"ordinary follow-up accepted without a new offense");
        var renewed=Issue(store,person,"I will keep harassing you after you told me to stop.",12);
        object nextSource=new object();
        Check(!store.TryCommitNpcConversationBoundary(renewed,renewed.Receipt,nextSource,
            Classify(nextSource,person,renewed.ExactText,"DirectInsult"),out _),
            "foreground cannot omit the saved standing refusal");
        Check(store.TryCommitNpcConversationBoundary(renewed,renewed.Receipt,nextSource,
            Classify(nextSource,person,renewed.ExactText,"PersistentAbuse",true),out var report),"renewed insult commits a refusal");
        Equal(ConversationSpeechBoundaryAction.EndConversation,report.Action);
        Equal(NpcConversationBoundaryReportStatus.None,report.ReportStatus);
        Equal(first.Ordinal,report.ParentBoundaryOrdinal);
        Equal(first.ReceiptId,report.ParentBoundaryReceiptId);
        Check(!store.CanTalkToNpcAfterBoundary(person,12),"repeated insult blocks another talk during cooldown");
        Check(report.RefuseUntil>=12+2*86400 && report.RefuseUntil<=12+7*86400,"repeated insult cooldown is several bounded days");
        Check(Accept(store,renewed),"renewed speech accepted");
        var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
        Equal(first.Ordinal,restored.ReadNpcConversationBoundary(person,12).ParentBoundaryOrdinal);
        Check(restored.CanTalkToNpcAfterBoundary(person,report.RefuseUntil+1),"contact reopens after cooldown");
        Check(!restored.ShouldEnforceNpcConversationBoundary(person,report.RefuseUntil+1),"expired no-talk flag does not block careful conversation");
        Check(restored.ReadNpcRelationship(person,NpcDirectedRelationship.PlayerTargetId,report.RefuseUntil+1).ProjectedResentment>0,
            "contact reopening does not erase resentment");
        Check(!restored.TryCommitNpcConversationBoundary(renewed,renewed.Receipt,nextSource,
            Classify(nextSource,person,renewed.ExactText,"PersistentAbuse",true),out _),"reloaded report cannot replay");
        var forged=restored.Snapshot();
        forged.ConversationAttitudes.Find(row=>row.PersonId==person).Boundary.ParentBoundaryKind=
            NpcExperienceSpeechActKind.DirectHarassment;
        Throws(()=>new WorldStateStore(0).Restore(forged));
        forged=restored.Snapshot();
        forged.NpcRelationships.AcceptedOutcomes.RemoveAll(row=>row.ConversationBoundaryOrdinal==first.Ordinal);
        Throws(()=>new WorldStateStore(0).Restore(forged));
    }

    private static void RepairPreservesOffense()
    {
        var store=Create(out string person);
        var insult=Issue(store,person,"You are a fool.",10);
        object firstSource=new object();
        Check(store.TryCommitNpcConversationBoundary(insult,insult.Receipt,firstSource,
            Classify(firstSource,person,insult.ExactText,"DirectInsult"),out var first),"first refusal");
        Check(Accept(store,insult),"first speech accepted");
        var apology=Issue(store,person,"I am sorry.",11);
        object repairSource=new object();
        Check(!store.TryRepairNpcConversationBoundary(apology,apology.Receipt,repairSource,
            Classify(new object(),person,apology.ExactText,"RepairAttempt",true)),
            "foreign repair classifier source rejected");
        Check(!store.TryRepairNpcConversationBoundary(apology,apology.Receipt,repairSource,
            Classify(repairSource,person,apology.ExactText,"RepairAttempt")),
            "repair without authoritative refusal context rejected");
        var repair=Classify(repairSource,person,apology.ExactText,"RepairAttempt",true);
        Check(store.TryRepairNpcConversationBoundary(apology,apology.Receipt,repairSource,repair),"repair accepted");
        Check(!store.ShouldEnforceNpcConversationBoundary(person,11),"refusal clears");
        Check(!store.TryRepairNpcConversationBoundary(apology,apology.Receipt,repairSource,repair),"repair cannot replay");
        Equal(first.ReceiptId,store.ReadNpcConversationBoundary(person,11).ReceiptId);
        Check(store.CaptureConversationMood(person,11).Value<=-40,"apology does not erase anger");
        Check(Accept(store,apology),"repair speech accepted");
        var mood=store.CaptureConversationMood(person,12);
        Check(store.TryCommitAcceptedSocialExchange(person,mood.AcceptedTurnSequence,mood.Revision,2,12,
            "date","place","I will explain.","I am still angry.",out _),"later ordinary exchange");
        Check(store.CaptureConversationMood(person,12).Value<=-38,
            "one mild social gain cannot erase the offense");
        Check(store.ReadNpcRelationship(person,NpcDirectedRelationship.PlayerTargetId,12).ProjectedResentment>=65,
            "later mild social turn cannot reset resentment");
        var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
        Check(!restored.ShouldEnforceNpcConversationBoundary(person,12),"repair persists");
        Check(restored.ReadNpcConversationBoundary(person,12).RepairedAtOrdinal==apology.Ordinal,
            "repair receipt highwater persists");
        Check(!restored.TryRepairNpcConversationBoundary(apology,apology.Receipt,repairSource,repair),
            "reloaded repair cannot replay");
    }

    private static void ProtectedRolesKeepFearPolicy()
    {
        var noble=Create(out string nobleId);
        Check(noble.RecordNpcRoleEvidence(nobleId,NpcSocialRank.Nobility,NpcCombatRole.Unknown,
            NpcServiceBinding.Unknown,"native-rank",null,null),"saved noble rank");
        CommitThreat(noble,nobleId);
        Equal(0,noble.ReadNpcRelationship(nobleId,NpcDirectedRelationship.PlayerTargetId,10)
            .ProjectedInterpersonalFear);
        Check(noble.ReadNpcRelationship(nobleId,NpcDirectedRelationship.PlayerTargetId,10)
            .ProjectedResentment>=65,"noble still resents direct threat");
        var guard=Create(out string guardId);
        Check(guard.RecordNpcRoleEvidence(guardId,NpcSocialRank.Unknown,NpcCombatRole.Guard,
            NpcServiceBinding.Unknown,null,"native-combat",null),"saved guard role");
        CommitThreat(guard,guardId);
        Equal(0,guard.ReadNpcRelationship(guardId,NpcDirectedRelationship.PlayerTargetId,10)
            .ProjectedInterpersonalFear);
    }

    private static void VerbalCourtCharge()
    {
        var begin=typeof(WorldStateStore).GetMethod("TryBeginNpcVerbalCharge");
        var read=typeof(WorldStateStore).GetMethod("ReadNpcVerbalCharge");
        var clear=typeof(WorldStateStore).GetMethod("ClearNpcVerbalCharge");
        Check(begin!=null && read!=null && clear!=null,"receipt-bound verbal court case is missing");
        var store=Create(out string person);
        var ticket=Issue(store,person,"I will hurt you.",10);object source=new object();
        Check(store.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,
            Classify(source,person,ticket.ExactText,"DirectThreat"),out var boundary),"threat commits");
        Check(Accept(store,ticket),"threat accepted");
        Func<WorldStateStore,string,double,bool> issue=(s,receipt,now)=>(bool)begin.Invoke(s,new object[]{person,receipt,11,now});
        Check(!issue(store,boundary.ReceiptId,11),"pending report is not an arrest charge");
        Check(store.TryMarkNpcConversationBoundaryReport(person,boundary.ReceiptId,NpcConversationBoundaryReportStatus.Started),"report starts");
        Check(!issue(store,boundary.ReceiptId,11),"undelivered report is not a charge");
        Check(store.TryMarkNpcConversationBoundaryReport(person,boundary.ReceiptId,NpcConversationBoundaryReportStatus.Delivered),"report delivered");
        Check(!issue(store,"forged",11) && !issue(store,boundary.ReceiptId,9),"exact receipt and chronology required");
        Check(issue(store,boundary.ReceiptId,11),"delivered threat creates verbal charge");
        object charge=read.Invoke(store,null);
        var receiptField=charge.GetType().GetField("ReceiptId");
        receiptField.SetValue(charge,"changed-copy");
        Equal(boundary.ReceiptId,(string)receiptField.GetValue(read.Invoke(store,null)));
        var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
        Equal(boundary.ReceiptId,(string)receiptField.GetValue(read.Invoke(restored,null)));
        clear.Invoke(restored,null);Check(read.Invoke(restored,null)==null,"new native offense or release clears verbal exception");
        var snapshot=store.Snapshot();
        receiptField.SetValue(typeof(WorldState).GetField("NpcVerbalCharge").GetValue(snapshot),"forged");
        Throws(()=>new WorldStateStore(0).Restore(snapshot));
        var policy=typeof(WorldState).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcVerbalJusticePolicy");
        var chance=policy.GetMethod("DismissalChance");
        Equal(72,(int)chance.Invoke(null,new object[]{5,50,0}));
        Equal(95,(int)chance.Invoke(null,new object[]{80,80,0}));
        Equal(35,(int)chance.Invoke(null,new object[]{0,10,-100}));
    }

    private static void CommitThreat(WorldStateStore store,string person)
    {
        var ticket=Issue(store,person,"I will hurt you.",10);
        object source=new object();
        Check(store.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,
            Classify(source,person,ticket.ExactText,"DirectThreat"),out _),"threat commits");
    }

    private static WorldStateStore Create(out string person)
    {var store=new WorldStateStore(42);person=store.GetOrCreate(11,new[]{23},1).Households[0].Residents[0].Id;return store;}
    private static NpcObservedSpeechTicket Issue(WorldStateStore store,string person,string text,double now)
    {Check(store.TryBeginNpcObservedSpeechSession(person,"session:"+now,out var session),"session");
        Check(store.TryIssueNpcObservedSpeech(session,1,text,now,out var ticket),"ticket");return ticket;}
    private static NpcSpeechActClassification Classify(object source,string person,string text,string label,
        bool refusesFurtherCommunication=false)
    {var request=NpcSpeechActRequest.Create(source,person,"player",null,text,"",refusesFurtherCommunication);
        Check(NpcSpeechActClassification.TryAccept(request,label,out var result),"classification");return result;}
    private static bool Accept(WorldStateStore store,NpcObservedSpeechTicket ticket)
    {return store.TryAcceptNpcObservedSpeechAfterAppend(ticket,ticket.Receipt,NpcExperienceAppliedEffectsMask.None)!=
        NpcObservedSpeechAcceptanceResult.Rejected;}
    private static void Run(string name,Action body)
    {executed++;try{body();Console.WriteLine("PASS "+name);}catch(Exception error){failures++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    private static void Check(bool okay,string message){if(!okay)throw new Exception(message);}
    private static void Equal<T>(T expected,T actual){if(!Equals(expected,actual))throw new Exception("expected "+expected+", got "+actual);}
    private static void Throws(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("expected ArgumentException");}
}
