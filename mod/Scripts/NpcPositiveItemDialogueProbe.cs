using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Extends only the existing disposable native appraisal fixture.
    internal static class NpcPositiveItemDialogueProbe
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,ResidentActor actor,WorldStateStore store,
            DaggerfallTalkWindow talk,Action<DaggerfallTalkWindow> reopened)
        {
            Need(Array.IndexOf(Environment.GetCommandLineArgs(),NativeNpcPoisonAppraisalProbe.Flag)>=0,
                "Owned native appraisal flag required.");
            var game=GameManager.Instance;var player=game.PlayerEntity;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            var busy=typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy",Private);
            var suspended=typeof(PeopleOfTheIliacBayMod).GetField("experienceAppraisalsSuspended",Private);
            var submit=typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText",Private);
            Need((bool)suspended.GetValue(host),"Parent must own the appraisal pause.");
            int recipe=NativeNpcMedicalService.RecipeFor(CurePoison.EffectKey);
            Need(recipe>=0,"Native antidote recipe missing.");
            var gift=ItemBuilder.CreatePotion(recipe);
            player.Items.AddItem(gift,noStack:true);
            ulong uid=gift.UID;
            string request="Please take this "+gift.LongName+" as a gift from me.";
            var streets=UnityEngine.Object.FindObjectOfType<StreetPopulation>();
            var offered=streets.ConversationActionCandidates(actor.Resident.Id);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_POSITIVE_ITEM_SETUP: item="+gift.LongName+"; value="+gift.value+
                "; playerItems="+player.Items.Count+"; offered="+offered.Exists(o=>o.Kind==NpcActionKind.Transfer && o.Target.ItemId==uid.ToString() &&
                    o.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc));
            try
            {
                submit.Invoke(host,new object[]{talk,request});
                float until=Time.realtimeSinceStartup+120;
                while((bool)busy.GetValue(host) && Time.realtimeSinceStartup<until)yield return null;
                Need(!(bool)busy.GetValue(host),"Gift request did not finish.");
                var job=store.ActiveNpcAction(actor.Resident.Id);
                Need(job!=null && job.Kind==NpcActionKind.Transfer && job.Target.ItemId==uid.ToString() &&
                    job.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc,
                    "Actual gift request did not select the existing native item transfer.");
                Need(player.Items.GetItem(uid)!=null && owner.Binding.Items.GetItem(uid)==null,
                    "Gift moved while conversation was still open.");
                DaggerfallUI.Instance.PopToHUD();
                until=Time.realtimeSinceStartup+90;
                while(Time.realtimeSinceStartup<until && !NpcActionExecutor.IsTerminal(store.ReadNpcAction(job.Id)))yield return null;
                job=store.ReadNpcAction(job.Id);
                Need(job.Phase==NpcActionPhase.Completed && job.ItemTransferReceipt!=null,
                    "Native gift handover did not complete: "+job.Phase+" "+job.FailureReason);
                Need(player.Items.GetItem(uid)==null && owner.Binding.Items.GetItem(job.ItemTransferReceipt.RetainedDestinationItemUid)!=null,
                    "Actual gift ownership did not change.");
                Need(job.ItemTransferReceipt.NativeValuationStatus==NpcExperienceNativeValuationStatus.Known &&
                    job.ItemTransferReceipt.NativeGoldValue==gift.value,
                    "The native received item value was lost before appraisal.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_POSITIVE_ITEM_HANDOVER: action="+job.Id+"; receipt="+
                    job.ItemTransferReceipt.TransactionId+"; exactItem=true; actualPlayerToNpc=true");
                suspended.SetValue(host,false);
                NpcExperienceInboxEntry entry=null;bool observedRequest=false;
                until=Time.realtimeSinceStartup+180;
                while(Time.realtimeSinceStartup<until)
                {
                    var ticket=typeof(PeopleOfTheIliacBayMod).GetField("activeExperienceTicket",Private).GetValue(host) as NpcExperienceAppraisalTicket;
                    if(ticket!=null && ticket.Request.CanonicalEvidenceId==job.Id+":item-received" && !observedRequest)
                    {observedRequest=true;Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_POSITIVE_ITEM_APPRAISAL_PROMPT: "+ticket.Request.Prompt);}
                    entry=store.Snapshot().NpcExperienceInbox.Entries.Find(e=>e.ObserverId==actor.Resident.Id && e.OutcomeId==job.Id+":item-received");
                    if(entry!=null && entry.Status!=NpcExperienceInboxStatus.Pending)break;
                    yield return null;
                }
                suspended.SetValue(host,true);
                Need(observedRequest && entry!=null && entry.Status==NpcExperienceInboxStatus.Consumed && entry.Appraisal!=null,
                    "Normal production scheduler did not appraise the actual received item.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_POSITIVE_ITEM_APPRAISAL: proposedMood="+entry.Appraisal.MoodDelta+
                    "; appliedMood="+entry.Appraisal.AppliedMoodDelta+"; appliedAffinity="+entry.Appraisal.AppliedAffinityDelta+
                    "; responsible="+entry.ResponsibleActorId+"; value="+entry.NativeGoldValue);
                Need(entry.EventKind==NpcExperienceEventKind.ItemReceived && entry.ResponsibleActorId==NpcDirectedRelationship.PlayerTargetId &&
                    entry.ReceivedItemUid==job.ItemTransferReceipt.RetainedDestinationItemUid && entry.TransferQuantity==1 &&
                    entry.NativeGoldValue==gift.value && entry.Appraisal.TrustDelta==0,
                    "Received item lost its exact retained UID, quantity, value or responsible player, or granted unsupported trust.");
                var causes=store.DescribeRecentConversationMoodCauses(actor.Resident.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),600);
                bool positive=entry.Appraisal.AppliedMoodDelta>0;
                if(positive)Need(causes.Contains("still lifts your mood") && causes.Contains("responsible actor: player"),
                    "An accepted positive player influence was missing from mood retrieval.");
                yield return ConversationMoodProbe.OpenTalk(host,game,actor,value=>talk=value);
                reopened(talk);
                yield return NpcMoodCauseDialogueProbe.VerifyPositiveItemRecall(host,talk,busy,positive);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_RECEIVED_ITEM_DIALOGUE_PASS: actualGemma=true; nativeTransfer=true; normalAppraisal=true; nativeValue=true; playerAttributed=true; actualReplyLogged=true; positiveMood="+positive);
            }
            finally {suspended.SetValue(host,true);}
        }
        static void Need(bool condition,string message)
        {if(!condition)throw new InvalidOperationException("Positive item dialogue: "+message);}
    }
}
