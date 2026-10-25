using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Controlled completed greeting, real local speech and mixer. This tests
    // presentation after reopening Talk, not the model's choice of wording.
    internal static class ReturningGreetingVoiceProbe
    {
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,ResidentActor actor)
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var bridge=(BreezeTtsTrialBridge)typeof(PeopleOfTheIliacBayMod).GetField("breezeTtsTrial",flags).GetValue(host);
            if(!bridge || !bridge.VoiceEnabled)throw new InvalidOperationException("Returning greeting fixture requires enabled local NPC voice.");
            DaggerfallTalkWindow talk=null;
            yield return ConversationMoodProbe.OpenTalk(host,GameManager.Instance,actor,value=>talk=value);
            const string answer="Welcome back. We were looking at what we could trade.";
            PersonalTradeNativeProbe.CaptureTradeContext(host,talk,"The player returned after discussing a trade.");
            BreezeTtsTrialBridge.MixerPlaybackStartReceipt onset=null;
            BreezeTtsTrialBridge.PrePlaybackTerminalReceipt failed=null;
            Action<BreezeTtsTrialBridge.MixerPlaybackStartReceipt> started=value=>{if(value.ExactText==answer && value.PersonId==actor.Resident.Id)onset=value;};
            Action<BreezeTtsTrialBridge.PrePlaybackTerminalReceipt> ended=value=>{if(value.ExactText==answer && value.PersonId==actor.Resident.Id)failed=value;};
            bridge.MixerPlaybackStarted+=started;bridge.PrePlaybackTerminated+=ended;
            try
            {
                int before=bridge.PublishedRequestCount;
                typeof(PeopleOfTheIliacBayMod).GetMethod("AppendReturningGreeting",flags).Invoke(host,new object[]{talk,answer});
                if(bridge.PublishedRequestCount!=before+1)throw new InvalidOperationException("Returning greeting did not publish exactly one voice request.");
                float deadline=Time.realtimeSinceStartup+180;
                while(onset==null && failed==null && Time.realtimeSinceStartup<deadline)yield return null;
                if(onset==null || onset.FirstNonZeroMixerTicks<=0 || onset.SampleFrames<=0)
                    throw new InvalidOperationException("Returning greeting has no nonzero mixer onset: "+(failed==null?"timeout":failed.Reason));
                while(bridge.GetRequestState(onset.RequestId)==BreezeTtsTrialBridge.RequestState.Playing && Time.realtimeSinceStartup<deadline)yield return null;
                if(bridge.GetRequestState(onset.RequestId)!=BreezeTtsTrialBridge.RequestState.Completed)
                    throw new InvalidOperationException("Returning greeting playback did not complete.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_RETURN_GREETING_VOICE_PASS: reopenedTalk=true; exactText=true; nonzeroMixer=true; completed=true; controlledReply=true");
            }
            finally {bridge.MixerPlaybackStarted-=started;bridge.PrePlaybackTerminated-=ended;DaggerfallUI.Instance.PopToHUD();}
        }
    }
}
