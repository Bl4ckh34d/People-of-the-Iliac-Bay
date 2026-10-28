using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Proves ordinary social replies, one isolated transport outage, recovery,
    // and native transcript persistence on an explicitly owned fixture only.
    internal static class DialogueAvailabilityProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private const string Fallback=DialogueReplyFallback.UnavailableReply;
        private const string OutageText="I am making conversation.";
        private const string RecoveryText="That is good to hear.";
        private const string ProbeArgument="-peopleoftheiliacbay-dialogue-availability-probe";
        private static string armedCharacter,armedResidentId,armedNpcName,armedPlayerText;

        private sealed class TurnResult { public NpcMemoryEntry Entry; }
        private sealed class Evidence
        {
            public string CheckpointTitle;
            public string ResidentId;
            public string NpcName;
            public double BaselineClock;
            public Vector3 BaselinePosition;
            public bool ReturningGreetingPending;
            public float ReturningGreetingRetryAt;
            public int OnlineModelTurns;
            public int ModelRecoveryTurns;
        }

        private static readonly string[] OnlineTurns=
        {
            "I want to get to know you better so we are no strangers no more",
            "What do you mean?",
            "I dont understand",
            "What is wrong with you?"
        };

        internal static bool IsEnabled
        { get { return Array.IndexOf(Environment.GetCommandLineArgs(),ProbeArgument)>=0; } }

        public static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character,string fixtureSave)
        {
            var game=GameManager.Instance;
            if(!host||!streets||!game||character!="Hakon Ironhand"||
                game.PlayerEntity.Name!=character||fixtureSave!="People - Gothway Garden")
                throw new InvalidOperationException("Dialogue availability probe requires the owned People - Gothway Garden disposable fixture.");

            var evidence=new Evidence
            {
                BaselineClock=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),
                BaselinePosition=game.PlayerMotor.transform.position,
                ReturningGreetingPending=Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host,"returningGreetingPending"),CultureInfo.InvariantCulture),
                ReturningGreetingRetryAt=Convert.ToSingle(ConversationMoodProbe.ReadHostField(host,"returningGreetingRetryAt"),CultureInfo.InvariantCulture)
            };
            // Normal probe failures still reload the exact baseline and delete this probe's checkpoint.
            // Forced process termination can interrupt that asynchronous reload; the baseline is never overwritten,
            // while a checkpoint whose save completed just before termination may remain for manual cleanup.
            Exception probeFailure=null;
            IEnumerator test=RunCases(host,streets,character,fixtureSave,evidence);
            try
            {
                while(true)
                {
                    bool more; object current=null;
                    try { more=test.MoveNext(); if(more)current=test.Current; }
                    catch(Exception error) { probeFailure=error; break; }
                    if(!more)break;
                    yield return current;
                }
            }
            finally
            {
                Exception disposeFailure=null;
                try { var disposable=test as IDisposable; if(disposable!=null)disposable.Dispose(); }
                catch(Exception error) { disposeFailure=error; }
                Disarm();
                ConversationMoodProbe.CancelTransport(host);
                if(DaggerfallUI.Instance)DaggerfallUI.Instance.PopToHUD();
                if(disposeFailure!=null)
                {
                    if(probeFailure==null)probeFailure=disposeFailure;
                    else Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_AVAILABILITY_DISPOSE_FAIL: "+disposeFailure);
                }
            }

            Exception restoreFailure=null,cleanupFailure=null;
            try
            {
                IEnumerator restore=ConversationMoodProbe.LoadSave(host,streets,character,fixtureSave,true);
                try
                {
                    while(true)
                    {
                        bool more; object current=null;
                        try { more=restore.MoveNext(); if(more)current=restore.Current; }
                        catch(Exception error) { restoreFailure=error; break; }
                        if(!more)break;
                        yield return current;
                    }
                }
                finally
                {
                    try { var disposable=restore as IDisposable; if(disposable!=null)disposable.Dispose(); }
                    catch(Exception error) { if(restoreFailure==null)restoreFailure=error; else Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_AVAILABILITY_RESTORE_DISPOSE_FAIL: "+error); }
                }
                if(restoreFailure==null)
                {
                    try
                    {
                        double restoredClock=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                        if(Math.Abs(restoredClock-evidence.BaselineClock)>2d)
                            throw new InvalidOperationException("Exact baseline reload did not restore original game clock.");
                        if(!game.PlayerMotor||Vector3.Distance(game.PlayerMotor.transform.position,evidence.BaselinePosition)>0.05f)
                            throw new InvalidOperationException("Exact baseline reload did not restore original player position.");
                    }
                    catch(Exception error) { restoreFailure=error; }
                }
            }
            finally
            {
                try { typeof(PeopleOfTheIliacBayMod).GetField("returningGreetingPending",Private).SetValue(host,evidence.ReturningGreetingPending); }
                catch(Exception error) { cleanupFailure=error; }
                try { typeof(PeopleOfTheIliacBayMod).GetField("returningGreetingRetryAt",Private).SetValue(host,evidence.ReturningGreetingRetryAt); }
                catch(Exception error) { if(cleanupFailure==null)cleanupFailure=error; else Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_AVAILABILITY_GREETING_RESTORE_FAIL: "+error); }
                if(!string.IsNullOrEmpty(evidence.CheckpointTitle))
                {
                    try { ConversationMoodProbe.DeleteCheckpoint(game,character,evidence.CheckpointTitle); }
                    catch(Exception error) { if(cleanupFailure==null)cleanupFailure=error; else Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_AVAILABILITY_CHECKPOINT_DELETE_FAIL: "+error); }
                }
            }
            if(restoreFailure!=null)
            {
                if(cleanupFailure!=null)Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_AVAILABILITY_CLEANUP_FAIL_AFTER_RESTORE: "+cleanupFailure);
                throw new InvalidOperationException("Dialogue availability probe could not restore its exact baseline.",restoreFailure);
            }
            if(probeFailure!=null)
            {
                if(cleanupFailure!=null)Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_AVAILABILITY_CLEANUP_FAIL_AFTER_PROBE: "+cleanupFailure);
                throw new InvalidOperationException("Dialogue availability probe failed after baseline restoration.",probeFailure);
            }
            if(cleanupFailure!=null)throw new InvalidOperationException("Dialogue availability probe cleanup failed.",cleanupFailure);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_AVAILABILITY_PASS: online-model=4; outage=unavailable; recovery=model; exact-transcript=6; disk-reload=true; speaker-identity=true; tracked-effects=inventory-wallet-action-task-unchanged.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_AVAILABILITY_BASELINE_RESTORED: baseline-restored=true; checkpoint-deleted=true; actual-direct-social-model-turns="+evidence.OnlineModelTurns+"; recovery-turns="+evidence.ModelRecoveryTurns+".");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_AVAILABILITY_SOCIAL_ROUTE: route=direct-SubmitSocialText; online-model-turns="+evidence.OnlineModelTurns+"; recovery-model-turns="+evidence.ModelRecoveryTurns+"; outage-injection=consumed-once.");
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character,string fixtureSave,Evidence evidence)
        {
            var game=GameManager.Instance;
            if(game.PlayerEnterExit.IsPlayerInsideBuilding)
            {
                game.PlayerEnterExit.TransitionExterior();
                DaggerfallUI.Instance.PopToHUD();
                float exteriorDeadline=Time.realtimeSinceStartup+30f;
                while(game.PlayerEnterExit.IsPlayerInsideBuilding&&Time.realtimeSinceStartup<exteriorDeadline)yield return new WaitForSecondsRealtime(0.25f);
                if(game.PlayerEnterExit.IsPlayerInsideBuilding)throw new InvalidOperationException("Availability probe could not enter Gothway Garden exterior.");
            }
            var store=GetStore(host);
            if(store==null)throw new InvalidOperationException("Dialogue availability probe has no loaded world store.");
            var actor=FindEligibleActor(store);
            float deadline=Time.realtimeSinceStartup+45f;
            while(!actor&&Time.realtimeSinceStartup<deadline)
            {yield return new WaitForSecondsRealtime(0.25f);store=GetStore(host);actor=FindEligibleActor(store);}
            if(!actor)throw new InvalidOperationException("No eligible adult resident is available for dialogue availability acceptance.");
            float initialDistance=Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_AVAILABILITY_ACTOR: resident="+actor.Resident.Id+"; distance-before-stage="+initialDistance.ToString("0.00",CultureInfo.InvariantCulture)+".");
            ConversationMoodProbe.StagePlayerNearActor(streets,actor,game);
            evidence.ResidentId=actor.Resident.Id;evidence.NpcName=actor.Person.NameNPC;
            var questions=new List<string>(OnlineTurns){OutageText,RecoveryText};
            foreach(string question in questions)AssertNoExistingTurn(store,evidence.ResidentId,question);
            var baseline=CaptureEffects(game,store,evidence.ResidentId);
            DaggerfallTalkWindow talk=null;
            yield return ConversationMoodProbe.OpenTalk(host,game,actor,window=>talk=window);
            if(talk==null)throw new InvalidOperationException("Native Talk did not open for selected managed resident.");
            for(int i=0;i<OnlineTurns.Length;i++)
            {
                var turn=new TurnResult();
                yield return SubmitTurn(host,evidence.ResidentId,talk,OnlineTurns[i],turn);
                store=GetStore(host);RequireModelReply(turn.Entry,OnlineTurns[i]);
                AssertNoNewEffects(game,store,evidence.ResidentId,baseline);AssertSpeaker(host,talk,evidence.ResidentId,evidence.NpcName);
                evidence.OnlineModelTurns++;
            }
            store=GetStore(host);var outageBaseline=CaptureEffects(game,store,evidence.ResidentId);
            Arm(character,evidence.ResidentId,evidence.NpcName,OutageText);
            try
            {
                var outage=new TurnResult();yield return SubmitTurn(host,evidence.ResidentId,talk,OutageText,outage);
                if(!InjectionConsumed())throw new InvalidOperationException("One-shot isolated transport failure was not consumed by the exact outage turn.");
                if(outage.Entry==null||outage.Entry.Source!="unavailable"||outage.Entry.NpcText!=Fallback)
                    throw new InvalidOperationException("Injected transport outage did not persist the exact neutral unavailable fallback.");
                store=GetStore(host);AssertNoNewEffects(game,store,evidence.ResidentId,outageBaseline);
                AssertSpeaker(host,talk,evidence.ResidentId,evidence.NpcName);
            }
            finally { Disarm(); }
            store=GetStore(host);var recovery=new TurnResult();yield return SubmitTurn(host,evidence.ResidentId,talk,RecoveryText,recovery);
            store=GetStore(host);RequireModelReply(recovery.Entry,RecoveryText);evidence.ModelRecoveryTurns++;
            AssertNoNewEffects(game,store,evidence.ResidentId,baseline);AssertSpeaker(host,talk,evidence.ResidentId,evidence.NpcName);
            AssertAllTurns(store,evidence.ResidentId,questions);
            yield return ConversationMoodProbe.CloseTalk(host);
            evidence.CheckpointTitle="People - IMM09 availability "+Guid.NewGuid().ToString("N").Substring(0,8);
            game.SaveLoadManager.EnumerateSaves();
            if(game.SaveLoadManager.FindSaveFolderByNames(character,evidence.CheckpointTitle)>=0)
                throw new InvalidOperationException("Generated availability checkpoint title already exists; refusing overwrite.");
            yield return SaveAndReload(game,streets,character,evidence.CheckpointTitle);
            store=GetStore(host);actor=FindActor(evidence.ResidentId);
            deadline=Time.realtimeSinceStartup+35f;
            while(!actor&&Time.realtimeSinceStartup<deadline){yield return new WaitForSecondsRealtime(0.25f);actor=FindActor(evidence.ResidentId);}
            if(!actor||actor.Person.NameNPC!=evidence.NpcName||!actor.Resident.IsAlive)
                throw new InvalidOperationException("Exact native speaker identity did not survive availability checkpoint reload.");
            AssertAllTurns(store,evidence.ResidentId,questions);
            AssertNoNewEffects(game,store,evidence.ResidentId,baseline);
            if(evidence.OnlineModelTurns!=4||evidence.ModelRecoveryTurns!=1)
                throw new InvalidOperationException("Native availability probe did not complete its exact four online turns plus one recovery turn.");
        }

        internal static void Arm(string character,string residentId,string npcName,string playerText)
        {
            if(!IsEnabled||!NativeProbeText.IsTestCharacter(character??string.Empty)||
                string.IsNullOrWhiteSpace(residentId)||
                string.IsNullOrWhiteSpace(npcName)||playerText!=OutageText)
                throw new InvalidOperationException("Transport injection requires the opt-in owned fixture and exact outage request.");
            armedCharacter=character;armedResidentId=residentId;armedNpcName=npcName;armedPlayerText=playerText;
        }

        internal static LocalDialogueConnection ConnectionFor(LocalDialogueConnection connection,DialogueRequest request)
        {
            if(connection==null||request==null||!IsEnabled||armedResidentId==null||
                request.PlayerText!=armedPlayerText||request.NpcName!=armedNpcName)return connection;
            var game=GameManager.Instance;
            var host=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var personField=typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId",Private);
            if(game==null||game.PlayerEntity==null||game.PlayerEntity.Name!=armedCharacter||!host||personField==null||
                (string)personField.GetValue(host)!=armedResidentId)return connection;
            armedResidentId=null;
            return new LocalDialogueConnection("http://127.0.0.1:1/completion",connection.TimeoutMilliseconds);
        }

        internal static bool InjectionConsumed(){return armedResidentId==null;}
        internal static void Disarm(){armedCharacter=armedResidentId=armedNpcName=armedPlayerText=null;}

        private static void RequireModelReply(NpcMemoryEntry entry,string question)
        {
            if(entry==null||entry.PlayerText!=question||entry.Source!="model"||string.IsNullOrWhiteSpace(entry.NpcText)||
                entry.NpcText==Fallback||entry.NpcText==FreeTextDialogue.FallbackAnswer)
                throw new InvalidOperationException("Native model reply was not persisted for an exact harmless utterance.");
        }

        private static void AssertAllTurns(WorldStateStore store,string residentId,IList<string> questions)
        {
            var transcript=store.ConversationTranscript(residentId,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()+2,256);
            foreach(string question in questions)
            {
                var matches=transcript.Where(entry=>entry.Source!="observed"&&entry.PlayerText==question).ToList();
                if(matches.Count!=1||string.IsNullOrWhiteSpace(matches[0].NpcText))
                    throw new InvalidOperationException("Exact social utterance/reply did not persist once after disk reload: "+question);
            }
            foreach(string question in OnlineTurns.Concat(new[]{RecoveryText}))
            {
                var entry=transcript.Single(value=>value.Source!="observed"&&value.PlayerText==question);
                if(entry.Source!="model"||entry.NpcText==Fallback||entry.NpcText==FreeTextDialogue.FallbackAnswer)
                    throw new InvalidOperationException("Recovered or online social turn is not a genuine saved model reply.");
            }
            var outage=transcript.Single(value=>value.Source!="observed"&&value.PlayerText==OutageText);
            if(outage.Source!="unavailable"||outage.NpcText!=Fallback)
                throw new InvalidOperationException("Unavailable outage transcript lost its explicit failure provenance.");
        }

        private sealed class EffectSnapshot
        {
            public int PlayerGold;
            public string PlayerItems,NpcItems;
            public HashSet<string> ActionIds,TaskIds;
        }

        private static EffectSnapshot CaptureEffects(GameManager game,WorldStateStore store,string residentId)
        {
            var state=store.Snapshot();
            var inventory=store.ReadNpcInventory(residentId);
            return new EffectSnapshot
            {
                PlayerGold=game.PlayerEntity.GetGoldAmount(),
                PlayerItems=ItemSignature(game.PlayerEntity.Items),
                NpcItems=NpcInventorySignature(inventory),
                ActionIds=new HashSet<string>(state.NpcActions.Where(value=>value.ResidentId==residentId).Select(value=>value.Id),StringComparer.Ordinal),
                TaskIds=new HashSet<string>(state.NpcTasks.Where(value=>value.ResidentId==residentId).Select(value=>value.Id),StringComparer.Ordinal)
            };
        }

        private static void AssertNoNewEffects(GameManager game,WorldStateStore store,string residentId,EffectSnapshot before)
        {
            var after=CaptureEffects(game,store,residentId);
            if(after.PlayerGold!=before.PlayerGold||after.PlayerItems!=before.PlayerItems||after.NpcItems!=before.NpcItems||
                after.ActionIds.Except(before.ActionIds).Any()||after.TaskIds.Except(before.TaskIds).Any()||
                store.ActiveNpcAction(residentId)!=null||store.ActiveNpcTask(residentId)!=null)
                throw new InvalidOperationException("Harmless social turn changed inventory, wallet, or action/task state.");
        }

        private static string ItemSignature(DaggerfallWorkshop.Game.Items.ItemCollection items)
        {
            var values=new List<string>();
            if(items!=null)for(int i=0;i<items.Count;i++)
            {var item=items.GetItem(i);if(item!=null)values.Add(item.UID.ToString()+":"+item.stackCount.ToString());}
            values.Sort(StringComparer.Ordinal);return string.Join("|",values);
        }

        private static string NpcInventorySignature(NpcInventoryState inventory)
        {
            if(inventory==null)return "<none>";
            var values=inventory.Items.Select(value=>value.Uid.ToString()+":"+value.NativePayload).OrderBy(value=>value,StringComparer.Ordinal);
            return inventory.WalletGold.ToString()+"|"+string.Join("|",values);
        }

        private static void AssertSpeaker(PeopleOfTheIliacBayMod host,DaggerfallTalkWindow talk,string residentId,string npcName)
        {
            var actor=FindActor(residentId);
            if(DaggerfallUI.UIManager.TopWindow!=talk||!IsSessionOpen(host)||GetPersonId(host)!=residentId||
                !actor||!actor.Person||!actor.Resident.IsAlive||actor.Person.NameNPC!=npcName)
                throw new InvalidOperationException("Dialogue availability turn changed speaker identity or native Talk window.");
        }

        private static IEnumerator OpenNativeTalk(GameManager game,PeopleOfTheIliacBayMod host,ResidentActor actor,Action<DaggerfallTalkWindow> ready)
        {
            if(!actor||!actor.Person||actor.Resident==null||!actor.Resident.IsAlive||actor.IsSleeping||actor.IsConversing)
                throw new InvalidOperationException("Selected resident is unavailable for native Talk.");
            string residentId=actor.Resident.Id;
            game.TalkManager.TalkToMobileNPC(actor.Person);
            float deadline=Time.realtimeSinceStartup+25f;
            while(!(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)&&Time.realtimeSinceStartup<deadline)yield return null;
            var talk=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            deadline=Time.realtimeSinceStartup+25f;
            while((talk==null||!talk.IsSetup||!IsSessionOpen(host)||IsBusy(host)||GetPersonId(host)!=residentId)&&Time.realtimeSinceStartup<deadline)yield return null;
            if(talk==null||game.TalkManager.CurrentNPCType!=TalkManager.NPCType.Mobile||game.TalkManager.MobileNPC!=actor.Person||
                !talk.IsSetup||!IsSessionOpen(host)||IsBusy(host)||GetPersonId(host)!=residentId)
                throw new InvalidOperationException("Native Talk did not open for the expected managed resident.");
            ready(talk);
        }

        private static IEnumerator SubmitTurn(PeopleOfTheIliacBayMod host,string residentId,DaggerfallTalkWindow talk,string text,TurnResult result)
        {
            if(talk==null||DaggerfallUI.UIManager.TopWindow!=talk||!IsSessionOpen(host)||GetPersonId(host)!=residentId||
                !ReferenceEquals(ConversationMoodProbe.ReadHostField(host,"memoryWindow"),talk)||
                !string.Equals(Convert.ToString(ConversationMoodProbe.ReadHostField(host,"memoryPersonId"),CultureInfo.InvariantCulture),residentId,StringComparison.Ordinal))
                throw new InvalidOperationException("Native Talk speaker changed before exact utterance submission.");
            var submit=typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitSocialText",Private);
            var historyMethod=typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory",BindingFlags.Static|BindingFlags.NonPublic);
            if(submit==null||submit.GetParameters().Length!=9||historyMethod==null)
                throw new InvalidOperationException("Production SubmitSocialText entry point or conversation history reader is unavailable.");
            var client=ConversationMoodProbe.ReadHostField(host,"localDialogue") as LocalDialogueClient;
            if(!client)throw new InvalidOperationException("Local dialogue client is unavailable for a measured social turn.");
            long requestBefore=ConversationMoodProbe.ReadLongProperty(client,"ReplyRequestCount");
            long completionBefore=ConversationMoodProbe.ReadLongProperty(client,"ReplyCompletionCount");
            var store=GetStore(host);
            long moodSequenceBefore=store.CaptureConversationMood(residentId).AcceptedTurnSequence;
            string history=Convert.ToString(historyMethod.Invoke(null,new object[]{talk}),CultureInfo.InvariantCulture)??string.Empty;
            object snapshot=ConversationMoodProbe.CaptureSnapshot(host,talk,text);
            typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy",Private).SetValue(host,false);
            submit.Invoke(host,new object[]{talk,text,history,null,string.Empty,null,false,null,snapshot});
            float deadline=Time.realtimeSinceStartup+75f;
            while(Time.realtimeSinceStartup<deadline)
            {
                store=GetStore(host);
                var matches=store.ConversationTranscript(residentId,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()+2,256)
                    .Where(entry=>entry.Source!="observed"&&entry.PlayerText==text).ToList();
                long requests=ConversationMoodProbe.ReadLongProperty(client,"ReplyRequestCount");
                long completions=ConversationMoodProbe.ReadLongProperty(client,"ReplyCompletionCount");
                bool transportControl=text==OutageText;
                if(requests>requestBefore&&(transportControl||completions>completionBefore)&&matches.Count>0&&!IsBusy(host))
                {
                    var candidate=matches[matches.Count-1];
                    if(VisibleExchangeMatches(talk,text,candidate.NpcText)){result.Entry=candidate;break;}
                }
                if(DaggerfallUI.UIManager.TopWindow!=talk||!IsSessionOpen(host)||GetPersonId(host)!=residentId)
                    throw new InvalidOperationException("Native reply changed or closed the active conversation unexpectedly.");
                yield return null;
            }
            long requestDelta=ConversationMoodProbe.ReadLongProperty(client,"ReplyRequestCount")-requestBefore;
            long completionDelta=ConversationMoodProbe.ReadLongProperty(client,"ReplyCompletionCount")-completionBefore;
            bool isTransportControl=text==OutageText;
            if(requestDelta!=1||(!isTransportControl&&completionDelta!=1))
                throw new InvalidOperationException("Exact social turn did not produce exactly one actual local-model request and completion. requests="+requestDelta+"; completions="+completionDelta+"; text="+text);
            if(result.Entry==null)throw new InvalidOperationException("Native social response did not complete and render for the exact submitted utterance.");
            var moodAfter=GetStore(host).CaptureConversationMood(residentId);
            if(moodAfter.AcceptedTurnSequence<moodSequenceBefore)
                throw new InvalidOperationException("Social mood sequence moved backward during actual model completion.");
        }

        private static bool VisibleExchangeMatches(DaggerfallTalkWindow talk,string question,string answer)
        {
            var field=typeof(PeopleOfTheIliacBayMod).GetField("conversationListField",BindingFlags.Static|BindingFlags.NonPublic);
            var listField=field==null?null:field.GetValue(null) as FieldInfo;
            var transcript=talk==null||listField==null?null:listField.GetValue(talk) as ListBox;
            if(transcript==null||string.IsNullOrWhiteSpace(answer))return false;
            string q=Normalize(question),a=Normalize(answer);bool found=false;
            for(int i=0;i<transcript.Count;i++)
            {
                var item=transcript.GetItem(i);if(item==null||item.textLabel==null)continue;
                string visible=Normalize(item.textLabel.Text);if(visible.Length==0)continue;
                if(!found){if(item.textColor==DaggerfallUI.DaggerfallQuestionTextColor&&visible==q)found=true;continue;}
                return item.textColor!=DaggerfallUI.DaggerfallQuestionTextColor&&visible==a;
            }
            return false;
        }

        private static string Normalize(string value)
        {
            if(string.IsNullOrEmpty(value))return string.Empty;
            var result=new System.Text.StringBuilder(value.Length);bool space=false;
            foreach(char c in value){if(char.IsWhiteSpace(c)){space=result.Length>0;continue;}if(space)result.Append(' ');result.Append(c);space=false;}
            return result.ToString();
        }

        private static IEnumerator SaveAndReload(GameManager game,StreetPopulation streets,string character,string title)
        {
            bool saved=false,loaded=false;
            SaveLoadManager.OnSaveEventHandler onSave=ignored=>saved=true;
            SaveLoadManager.OnLoadEventHandler onLoad=ignored=>loaded=true;
            SaveLoadManager.OnSave+=onSave;SaveLoadManager.OnLoad+=onLoad;
            try
            {
                game.SaveLoadManager.Save(character,title);
                float deadline=Time.realtimeSinceStartup+60f;
                while(!saved&&Time.realtimeSinceStartup<deadline)yield return null;
                if(!saved)throw new InvalidOperationException("Dialogue availability native save timed out.");
                game.SaveLoadManager.EnumerateSaves();
                int slot=game.SaveLoadManager.FindSaveFolderByNames(character,title);
                if(slot<0)throw new InvalidOperationException("Dialogue availability native save missing.");
                game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();
                deadline=Time.realtimeSinceStartup+75f;
                while((!loaded||!streets.Ready)&&Time.realtimeSinceStartup<deadline)yield return null;
                if(!loaded||!streets.Ready)throw new InvalidOperationException("Dialogue availability native disk reload timed out.");
            }
            finally{SaveLoadManager.OnSave-=onSave;SaveLoadManager.OnLoad-=onLoad;}
            yield return null;
        }

        private static void AssertNoExistingTurn(WorldStateStore store,string residentId,string text)
        {
            if(store.ConversationTranscript(residentId,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()+2,256)
                .Any(entry=>entry.Source!="observed"&&entry.PlayerText==text))
                throw new InvalidOperationException("Fixture already contains an availability probe utterance; refusing stale-result pass.");
        }

        private static ResidentActor FindEligibleActor(WorldStateStore store)
        {
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if(!actor||!actor.Person||actor.Resident==null||!actor.Resident.IsAlive||actor.Resident.AgeGroup!=ResidentAgeGroup.Adult||
                    actor.Resident.IsQuestProtected||actor.IsConversing||actor.IsSleeping)continue;
                if(store.ActiveNpcAction(actor.Resident.Id)!=null||store.ActiveNpcTask(actor.Resident.Id)!=null||
                    store.TryGetProactiveContactOffer(actor.Resident.Id,now,out _)||store.GetDialogueComplaint(actor.Resident.Id)!=null)continue;
                if(store.TryGetActualConversationDisposition(actor.Resident.Id,out int disposition)&&disposition<=-100)continue;
                return actor;
            }
            return null;
        }

        private static ResidentActor FindActor(string residentId)
        {return UnityEngine.Object.FindObjectsOfType<ResidentActor>().FirstOrDefault(actor=>actor&&actor.Person&&actor.Resident!=null&&actor.Resident.Id==residentId);}
        private static WorldStateStore GetStore(PeopleOfTheIliacBayMod host)
        {return typeof(PeopleOfTheIliacBayMod).GetField("store",Private).GetValue(host) as WorldStateStore;}
        private static string GetPersonId(PeopleOfTheIliacBayMod host)
        {return (string)typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId",Private).GetValue(host);}
        private static bool IsBusy(PeopleOfTheIliacBayMod host)
        {return (bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy",Private).GetValue(host);}
        private static bool IsSessionOpen(PeopleOfTheIliacBayMod host)
        {return (bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen",Private).GetValue(host);}
    }
}
