using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Controlled semantic classification; all travel and dispatch use normal updates.
        internal IEnumerator VerifyDialogueComplaint()
        {
            bool verifyWaiting=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-verify-complaint-wait")>=0;
            var game=GameManager.Instance;var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((43200-clock.ToSeconds()%86400+86400)%86400));
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            var host=GetComponent<PeopleOfTheIliacBayMod>();
            typeof(PeopleOfTheIliacBayMod).GetField("exteriorGuardsEnabled",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(host,true);
            var guards=GetComponent<GuardPopulation>();float deadline=Time.realtimeSinceStartup+180;
            while((!guards || !guards.OwnsSettlement || !guards.HasAvailableDialogueGuards()) && Time.realtimeSinceStartup<deadline)
                yield return null;
            if(!guards || !guards.HasAvailableDialogueGuards())throw new InvalidOperationException("Complaint fixture has no ready guard roster.");
            if(game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None)
                throw new InvalidOperationException("Complaint fixture requires no existing player crime.");
            ResidentActor witness=null;float shortest=float.PositiveInfinity;Vector3 guardPosition=Vector3.zero;
            deadline=Time.realtimeSinceStartup+60;
            while(!witness && Time.realtimeSinceStartup<deadline)
            {
                foreach(var actor in new List<ResidentActor>(actors.Values))
                {
                    if(!actor || !actor.gameObject.activeInHierarchy || actor.IsSleeping || actor.IsConversing ||
                        !actor.Resident.IsAlive || actor.Resident.IsQuestProtected || actor.Resident.AgeGroup!=ResidentAgeGroup.Adult ||
                        worldStore.ActiveNpcAction(actor.Resident.Id)!=null ||
                        worldStore.ReadNpcConversationBoundary(actor.Resident.Id,clock.ToSeconds())!=null)continue;
                    string guardId;Vector3 target;List<SavedRoutePoint> route;
                    Vector3 ground=actor.transform.position-Vector3.up;
                    if(!guards.TryFindNearestComplaintGuard(ground,out guardId,out target,out route) ||
                        Vector3.Distance(ground,target)<5)continue;
                    float length=0;
                    for(int i=1;i<route.Count;i++)length+=Vector2.Distance(new Vector2((float)route[i-1].X,(float)route[i-1].Z),
                        new Vector2((float)route[i].X,(float)route[i].Z));
                    if(length>=shortest || length>100)continue;
                    shortest=length;witness=actor;guardPosition=target;
                }
                if(!witness)yield return null;
            }
            if(!witness)throw new InvalidOperationException("No actual adult has a nearby reachable complaint guard.");
            string person=witness.Resident.Id;int guardCount=guards.Roster.Guards.Count;
            // A side offset may land inside a building. Verify the incident point
            // before using it; an unreachable test placement cannot prove delivery.
            bool incidentPlaced=false;Vector3? unavailableIncident=null;var crowd=witness.GetComponent<CrowdMotion>();
            var guardLocal=directory.transform.InverseTransformPoint(guardPosition);
            foreach(var offset in new[]{Vector3.right*3,Vector3.left*3,Vector3.forward*3,Vector3.back*3})
            {
                var candidate=directory.transform.InverseTransformPoint(witness.transform.position-Vector3.up+offset);
                var ground=geometry.ScenePoint(new RoutePoint(candidate.x,candidate.z));
                var response=geometry.RouteBetweenPoints(new SavedRoutePoint {X=guardLocal.x,Z=guardLocal.z},
                    new SavedRoutePoint {X=candidate.x,Z=candidate.z});
                if(response==null || response.Count<2){unavailableIncident=directory.transform.InverseTransformPoint(ground);continue;}
                if(incidentPlaced || !crowd || !crowd.CanTraverseTo(ground))continue;
                game.PlayerMotor.transform.position=ground+Vector3.up;Physics.SyncTransforms();incidentPlaced=true;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPLAINT_INCIDENT: ground="+ground+"; responseWaypoints="+response.Count);
                if(!verifyWaiting || unavailableIncident.HasValue)break;
            }
            if(!incidentPlaced)throw new InvalidOperationException("No collision-clear reachable incident placement beside the witness.");
            if(verifyWaiting && !unavailableIncident.HasValue)throw new InvalidOperationException("Waiting fixture lacks a deliberately unreachable response point.");
            if(StartDialogueComplaint(witness,person) || guards.TryReportLegacyDialogueComplaintAt(witness.transform.position))
                throw new InvalidOperationException("Receiptless dislike started a police report.");
            NpcObservedSpeechSession session;NpcObservedSpeechTicket ticket;NpcSpeechActClassification classification;
            NpcConversationBoundaryRecord boundary;object source=new object();const string threat="I will hurt you.";
            double now=clock.ToSeconds();
            if(!worldStore.TryBeginNpcObservedSpeechSession(person,"native-complaint:"+now,out session))
                throw new InvalidOperationException("Complaint speech session unavailable.");
            try
            {
                if(!worldStore.TryIssueNpcObservedSpeech(session,1,threat,now,out ticket) ||
                    !NpcSpeechActClassification.TryAccept(NpcSpeechActRequest.Create(source,person,"player",null,threat,""),
                        "DirectThreat",out classification) ||
                    !worldStore.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,classification,out boundary) ||
                    worldStore.TryAcceptNpcObservedSpeechAfterAppend(ticket,ticket.Receipt,NpcExperienceAppliedEffectsMask.None)==
                        NpcObservedSpeechAcceptanceResult.Rejected || !StartDialogueComplaint(witness,person,boundary.ReceiptId))
                    throw new InvalidOperationException("Controlled threat did not start its physical complaint.");
                Vector3 previous=witness.transform.position;double travelled=0;float maxStep=0;
                float started=Time.realtimeSinceStartup,nextTrace=started+10;bool restoredProgress=false;
                float waitingSince=-1,waitSeconds=0,maxWaitDrift=0;double waitingTravel=0;
                Vector3 waitPosition=Vector3.zero;bool waitingPassed=false;
                var first=worldStore.GetDialogueComplaint(person);
                var realIncident=new Vector3((float)first.CrimeX,(float)first.CrimeY,(float)first.CrimeZ);
                if(verifyWaiting)
                {
                    // Fault injection: temporarily make response routing unavailable,
                    // then restore the real captured scene after the standing check.
                    // The player is NOT moved into this deliberately invalid location.
                    first.CrimeX=unavailableIncident.Value.x;first.CrimeY=unavailableIncident.Value.y;first.CrimeZ=unavailableIncident.Value.z;
                    worldStore.SetDialogueComplaint(first);
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPLAINT_START: resident="+person+"; guard="+first.GuardId+
                    "; routeMetres="+shortest+"; initialMetres="+Vector3.Distance(previous-Vector3.up,guardPosition));
                deadline=started+120;
                while(Time.realtimeSinceStartup<deadline)
                {
                    yield return null;
                    witness=FindActor(person);
                    if(!witness || !witness.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("Complaint body disappeared during visible travel.");
                    Vector3 current=witness.transform.position;float step=Vector3.Distance(previous,current);
                    travelled+=step;maxStep=Mathf.Max(maxStep,step);previous=current;
                    if(step>0.9f)throw new InvalidOperationException("Complaint runner jumped: "+step);
                    var pending=worldStore.GetDialogueComplaint(person);
                    var state=worldStore.ReadNpcConversationBoundary(person,clock.ToSeconds());
                    if(pending==null)
                    {
                        double seconds=Time.realtimeSinceStartup-started-waitSeconds;double kmh=(travelled-waitingTravel)/seconds*3.6;
                        var report=guards.Roster.Reports.Find(row=>row.Id=="dialogue:"+boundary.ReceiptId);
                        var charge=worldStore.ReadNpcVerbalCharge();
                        if(state.ReportStatus!=NpcConversationBoundaryReportStatus.Delivered || report==null ||
                            report.GuardIds.Count==0 || charge==null || charge.ReceiptId!=boundary.ReceiptId ||
                            game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.Assault ||
                            guards.Roster.Guards.Count!=guardCount || travelled<2 || kmh<3.5 || !restoredProgress || verifyWaiting&&!waitingPassed ||
                            Vector3.Distance(current-Vector3.up,guardPosition)>2.5f)
                            throw new InvalidOperationException("Complaint delivery failed: status="+state.ReportStatus+
                                "; kmh="+kmh+"; travel="+travelled+"; report="+(report!=null)+"; charge="+(charge!=null));
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPLAINT_PASS: travelMetres="+travelled+"; realSeconds="+seconds+
                            "; averageKmh="+kmh+"; maxStep="+maxStep+"; bodyProgress=true; restore=true; delayedCharge=true; finiteGuards=true; LLMSelectionNotTested=true");
                        yield break;
                    }
                    var local=directory.transform.InverseTransformPoint(current-Vector3.up);
                    if(Vector3.Distance(local,new Vector3((float)pending.X,(float)pending.Y,(float)pending.Z))>0.1f ||
                        game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None ||
                        state.ReportStatus!=NpcConversationBoundaryReportStatus.Started ||
                        pending.CrimeX!=first.CrimeX || pending.CrimeY!=first.CrimeY || pending.CrimeZ!=first.CrimeZ)
                        throw new InvalidOperationException("Complaint progress diverged from its body or charged before delivery.");
                    guards.TryGetComplaintGuardPosition(pending.GuardId,out guardPosition);
                    if(verifyWaiting && !waitingPassed && Vector3.Distance(current-Vector3.up,guardPosition)<=2.25f)
                    {
                        if(waitingSince<0){waitingSince=Time.realtimeSinceStartup;waitPosition=current;}
                        else {waitingTravel+=step;maxWaitDrift=Mathf.Max(maxWaitDrift,Vector3.Distance(current,waitPosition));}
                        if(Time.realtimeSinceStartup-waitingSince>=5)
                        {
                            var facing=guardPosition-(current-Vector3.up);facing.y=0;
                            float dot=Vector3.Dot(witness.transform.forward,facing.normalized);
                            var billboard=witness.Person.Asset;
                            bool stopped=billboard && (billboard.IsIdle || (float)typeof(MobilePersonBillboard).GetField("animSpeed",
                                BindingFlags.Instance|BindingFlags.NonPublic).GetValue(billboard)==0);
                            if(waitingTravel>0.75 || maxWaitDrift>0.35f || dot<0.95f || !stopped)
                                throw new InvalidOperationException("Failed complaint did not wait at guard: travel="+waitingTravel+
                                    "; drift="+maxWaitDrift+"; facing="+dot+"; stopped="+stopped);
                            waitSeconds=Time.realtimeSinceStartup-waitingSince;waitingPassed=true;
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPLAINT_WAIT_PASS: seconds="+waitSeconds+"; travel="+waitingTravel+
                                "; drift="+maxWaitDrift+"; facing="+dot+"; stopped=true; noCharge=true; responseLocationFaultInjected=true");
                            pending.CrimeX=realIncident.x;pending.CrimeY=realIncident.y;pending.CrimeZ=realIncident.z;
                            worldStore.SetDialogueComplaint(pending);first=pending;
                        }
                    }
                    if(Time.realtimeSinceStartup>=nextTrace)
                    {
                        nextTrace+=20;
                        Vector3 ground=current-Vector3.up;
                        var waypoint=pending.Route[Math.Min(pending.NextWaypoint,pending.Route.Count-1)];
                        var target=geometry.ScenePoint(new RoutePoint((float)waypoint.X,(float)waypoint.Z));target.y=ground.y;
                        var direction=(target-ground).normalized;string obstacles="";
                        foreach(var hit in Physics.CapsuleCastAll(ground+Vector3.up*0.3f,ground+Vector3.up*1.65f,
                            0.22f,direction,0.25f,~0,QueryTriggerInteraction.Ignore))
                            if(CivilianResponses.Blocker(hit.collider))obstacles+=hit.collider.name+":"+hit.distance+";";
                        var report=guards.Roster.Reports.Find(row=>row.Id=="dialogue:"+boundary.ReceiptId);
                        var guard=guards.Roster.Guards.Find(row=>row.Id==pending.GuardId);
                        var incident=new SavedRoutePoint {X=pending.CrimeX,Z=pending.CrimeZ};
                        var responseRoute=geometry.RouteBetweenPoints(guards.Roster.Position(pending.GuardId,clock.ToSeconds()),incident);
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPLAINT_PROGRESS: travel="+travelled+"; ground="+ground+
                            "; target="+target+"; waypoint="+pending.NextWaypoint+"; guardDistance="+Vector3.Distance(ground,guardPosition)+
                            "; blockers="+obstacles+"; conversing="+witness.IsConversing+"; reaction="+(Reaction(person,clock.ToSeconds())!=null)+
                            "; report="+(report==null?-1:report.GuardIds.Count)+"; responseRoute="+(responseRoute==null?-1:responseRoute.Count)+
                            "; guardDuty="+guard.Duty+"; complaintTown="+pending.SettlementId+"; guardTown="+guards.Roster.SettlementId);
                    }
                    if(!restoredProgress && travelled>1)
                    {
                        var copy=new WorldStateStore(0);copy.Restore(worldStore.Snapshot());
                        var saved=copy.GetDialogueComplaint(person);
                        if(saved==null || saved.SourceReceiptId!=boundary.ReceiptId || saved.X!=pending.X || saved.Z!=pending.Z ||
                            saved.NextWaypoint!=pending.NextWaypoint ||
                            copy.ReadNpcConversationBoundary(person,clock.ToSeconds()).ReportStatus!=NpcConversationBoundaryReportStatus.Started)
                            throw new InvalidOperationException("Mid-route snapshot lost complaint progress.");
                        restoredProgress=true;
                    }
                }
                var stalled=worldStore.GetDialogueComplaint(person);
                throw new InvalidOperationException("Complaint travel timed out: metres="+travelled+"; waypoint="+stalled?.NextWaypoint+
                    "; guardDistance="+Vector3.Distance(previous-Vector3.up,guardPosition));
            }
            finally
            {
                worldStore.TryCloseNpcObservedSpeechSession(session);
                game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
            }
        }
    }
}
