using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        internal IEnumerator VerifyMurderReport()
        {
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
                    NpcActionDecision decision;
                    if(!NpcActionIntent.TryBind("DIRECT|ReportMurder|"+option.Id+"|1|900",options,out decision))
                        throw new InvalidOperationException("Report action cannot bind through the semantic contract.");
                    if(!NpcProbeCommandSource.TryAcceptAction(this,actor.Resident.Id,"murder-report-native",decision,
                        "Please find a guard and anonymously report a murder I witnessed.",out lastReason))continue;
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
            deadline=Time.realtimeSinceStartup+240;
            while(Time.realtimeSinceStartup<deadline)
            {
                var current=worldStore.ReadNpcAction(accepted.Id);
                if(NpcActionExecutor.IsTerminal(current))
                {
                    if(current.Phase!=NpcActionPhase.Completed)
                        throw new InvalidOperationException("Native murder report ended without delivery: "+current.FailureReason);
                    var memories=worldStore.Snapshot().SceneMemories;
                    if(!memories.Exists(m=>m.ObserverId==residentId && m.EventId==current.Id) ||
                        !memories.Exists(m=>m.ObserverId==current.Target.PersonId && m.EventId==current.Id) ||
                        metres<2 || game.PlayerEntity.CrimeCommitted!=originalCrime)
                        throw new InvalidOperationException("Report delivery lacks physical travel/memories or changes player crime.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MURDER_REPORT_PASS: bodyTravelMetres="+metres+
                        "; bothMemories=true; anonymousHearsay=true; playerCrimeUnchanged=true; semanticBinding=true; LLMSelectionNotTested=true");
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
    }
}
