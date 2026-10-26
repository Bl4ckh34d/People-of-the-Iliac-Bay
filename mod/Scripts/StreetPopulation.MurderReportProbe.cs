using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        internal IEnumerator VerifyMurderReport()
        {
            bool liveChat=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-verify-murder-report-chat")>=0;
            var game=GameManager.Instance;var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((43200-clock.ToSeconds()%86400+86400)%86400));
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            var host=GetComponent<PeopleOfTheIliacBayMod>();
            typeof(PeopleOfTheIliacBayMod).GetField("exteriorGuardsEnabled",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(host,true);
            var controller=GetComponent<GuardPopulation>();
            float guardDeadline=Time.realtimeSinceStartup+180;
            while((!controller || !controller.OwnsSettlement || controller.Roster==null) && Time.realtimeSinceStartup<guardDeadline)
                yield return null;
            if(!controller || !controller.OwnsSettlement || controller.Roster==null)
                throw new InvalidOperationException("Finite guard fixture did not finish its native setup.");
            var originalCrime=game.PlayerEntity.CrimeCommitted;
            ResidentActor messenger=null;NpcActionJob accepted=null;string lastReason="No eligible resident.";
            int considered=0,offered=0;
            float deadline=Time.realtimeSinceStartup+60;
            while(!messenger && Time.realtimeSinceStartup<deadline)
            {
                foreach(var actor in new List<ResidentActor>(actors.Values))
                {
                    if(!actor || !actor.gameObject.activeInHierarchy || actor.IsConversing || actor.IsSleeping ||
                        actor.Resident.AgeGroup!=ResidentAgeGroup.Adult || !actor.Resident.IsAlive ||
                        worldStore.ActiveNpcAction(actor.Resident.Id)!=null)continue;
                    var ground=actor.transform.position-Vector3.up;
                    considered++;
                    game.PlayerMotor.transform.position=ground+Vector3.up+Vector3.right*3;
                    var options=ConversationActionCandidates(actor.Resident.Id);
                    var option=options.Find(row=>row.Kind==NpcActionKind.ReportMurder);
                    if(option==null)continue;
                    offered++;
                    Vector3 target;float range;
                    var proposed=new NpcActionJob {Kind=option.Kind,Target=option.Target};
                    if(!ResolveMurderReportGuard(proposed,out target,out range,out lastReason) ||
                        Vector3.Distance(ground,target)<5)continue;
                    if(liveChat)
                        yield return AcceptMurderReportThroughDialogue(host,actor);
                    else
                    {
                        NpcActionDecision decision;
                        if(!NpcActionIntent.TryBind("DIRECT|ReportMurder|"+option.Id+"|1|900",options,out decision))
                            throw new InvalidOperationException("Report action cannot bind through the semantic contract.");
                        if(!NpcProbeCommandSource.TryAcceptAction(this,actor.Resident.Id,"murder-report-native",decision,
                            "Please find a guard and anonymously report a murder I witnessed.",out lastReason))continue;
                    }
                    messenger=actor;accepted=worldStore.ActiveNpcAction(actor.Resident.Id);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MURDER_REPORT_START: resident="+actor.Resident.Id+
                        "; guard="+option.Target.PersonId+"; initialMetres="+Vector3.Distance(ground,target));
                    break;
                }
                yield return null;
            }
            if(!messenger || accepted==null)
            {
                var guards=GetComponent<GuardPopulation>();
                throw new InvalidOperationException("No native report accepted: "+lastReason+"; considered="+considered+
                    "; offered="+offered+"; residentBodies="+actors.Count+"; guardBodies="+(guards?guards.ActiveCount:-1)+
                    "; roster="+(guards && guards.Roster!=null?guards.Roster.Guards.Count:-1)+"; inside="+(bool)interior);
            }
            string residentId=messenger.Resident.Id;
            var start=messenger.transform.position;double metres=0;var previous=start;
            float travelStarted=Time.realtimeSinceStartup;
            deadline=Time.realtimeSinceStartup+240;
            while(Time.realtimeSinceStartup<deadline)
            {
                var current=worldStore.ReadNpcAction(accepted.Id);
                if(NpcActionExecutor.IsTerminal(current))
                {
                    if(current.Phase!=NpcActionPhase.Completed)
                        throw new InvalidOperationException("Native murder report ended without delivery: "+current.FailureReason+
                            "; metres="+metres+"; realSeconds="+(Time.realtimeSinceStartup-travelStarted));
                    var memories=worldStore.Snapshot().SceneMemories;
                    if(!memories.Exists(m=>m.ObserverId==residentId && m.EventId==current.Id) ||
                        !memories.Exists(m=>m.ObserverId==current.Target.PersonId && m.EventId==current.Id) ||
                        metres<2 || game.PlayerEntity.CrimeCommitted!=originalCrime)
                        throw new InvalidOperationException("Report delivery lacks physical travel/memories or changes player crime.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MURDER_REPORT_PASS: bodyTravelMetres="+metres+
                        "; bothMemories=true; anonymousHearsay=true; playerCrimeUnchanged=true; semanticBinding=true; LLMSelectionNotTested="+!liveChat);
                    yield break;
                }
                messenger=FindActor(residentId);
                if(messenger)
                {
                    var at=messenger.transform.position;float step=Vector3.Distance(previous,at);
                    if(step>0.7f)throw new InvalidOperationException("Report messenger jumped instead of walking: "+step);
                    metres+=step;previous=at;
                }
                yield return null;
            }
            throw new InvalidOperationException("Native murder report did not finish before the fixture deadline.");
        }

        private IEnumerator AcceptMurderReportThroughDialogue(PeopleOfTheIliacBayMod host,ResidentActor actor)
        {
            const BindingFlags privateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
            var hostType=typeof(PeopleOfTheIliacBayMod);var game=GameManager.Instance;
            if(!(bool)hostType.GetField("localDialogueEnabled",privateInstance).GetValue(host))
                throw new InvalidOperationException("Murder report chat requires the enabled real local dialogue client.");
            // Friendly social baseline, not forced consent. All interpretation,
            // willingness and admission go through the normal free-text handler.
            worldStore.SetConversationBaseline(actor.Resident.Id,95,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            DaggerfallTalkWindow window=null;
            try
            {
                yield return ConversationMoodProbe.OpenTalk(host,game,actor,value=>window=value);
                worldStore.SetConversationBaseline(actor.Resident.Id,95,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                hostType.GetField("conversationNativeDisposition",privateInstance).SetValue(host,95);
                typeof(TalkManager).GetField("reactionToPlayer",privateInstance).SetValue(game.TalkManager,
                    ConversationEffects.Reaction(95,worldStore.ConversationDisposition(actor.Resident.Id)));
                var before=actor.transform.position;
                const string request="I saw a murder by someone I don't know. Please find the nearest guard and report it anonymously.";
                hostType.GetMethod("SubmitFreeText",privateInstance).Invoke(host,new object[]{window,request});
                var busy=hostType.GetField("conversationBusy",privateInstance);
                float deadline=Time.realtimeSinceStartup+180;
                bool tracedSaved=false;
                while((bool)busy.GetValue(host) && Time.realtimeSinceStartup<deadline)
                {
                    if(!tracedSaved && worldStore.ActiveNpcAction(actor.Resident.Id)!=null)
                    {
                        tracedSaved=true;
                        var snapshot=(ConversationContextSnapshot)hostType.GetField("conversationContextCurrentSnapshot",privateInstance).GetValue(host);
                        var replyBinding=(ConversationContextBinding)hostType.GetField("conversationActionReplyBinding",privateInstance).GetValue(host);
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MURDER_REPORT_REPLY_STATE: current="+host.IsCurrentConversationContext(snapshot,window)+
                            "; replyCommitted="+(replyBinding!=null && replyBinding.HasSameRequestBinding(snapshot.Binding))+
                            "; stage="+hostType.GetField("conversationContextIssuedStageToken",privateInstance).GetValue(host)+
                            "; speaker="+snapshot.Binding.PersonId+"; textMatches="+
                            string.Equals(request,(string)hostType.GetField("conversationContextCapturedPlayerText",privateInstance).GetValue(host),StringComparison.Ordinal));
                    }
                    yield return null;
                }
                string transcript=(string)hostType.GetMethod("ReadConversationHistory",BindingFlags.Static|BindingFlags.NonPublic)
                    .Invoke(null,new object[]{window});
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MURDER_REPORT_CHAT: "+transcript);
                var action=worldStore.ActiveNpcAction(actor.Resident.Id);
                if((bool)busy.GetValue(host) || !transcript.Contains(request) || action==null ||
                    action.Kind!=NpcActionKind.ReportMurder || action.Phase!=NpcActionPhase.Accepted ||
                    worldStore.ActiveNpcTask(actor.Resident.Id)!=null)
                    throw new InvalidOperationException("Friendly native dialogue did not accept one murder report; busy="+busy.GetValue(host)+
                        "; task="+(worldStore.ActiveNpcTask(actor.Resident.Id)!=null)+"; stage="+
                        hostType.GetField("conversationContextIssuedStageToken",privateInstance).GetValue(host)+
                        "; captured="+hostType.GetField("conversationContextCapturedPlayerText",privateInstance).GetValue(host));
                if(Vector3.Distance(before,actor.transform.position)>0.05f)
                    throw new InvalidOperationException("Murder report messenger moved before Talk closed.");
                // Exceed the ordinary twenty-second walking-stall timeout while
                // Talk still owns the actor. This must not consume walking progress.
                float holdUntil=Time.realtimeSinceStartup+25;
                while(Time.realtimeSinceStartup<holdUntil)
                {
                    if(Vector3.Distance(before,actor.transform.position)>0.05f ||
                        worldStore.ReadNpcAction(action.Id).Phase!=NpcActionPhase.Accepted)
                        throw new InvalidOperationException("Accepted report moved or expired during the conversation hold.");
                    yield return null;
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MURDER_REPORT_CHAT_ACCEPTED: nativeWindow=true; freeText=true; savedAction="+
                    action.Id+"; heldDuringTalkSeconds=25; friendlyBaseline=95; returnGreetingSuppressed=true");
            }
            finally {DaggerfallUI.Instance.PopToHUD();}
        }
    }
}
