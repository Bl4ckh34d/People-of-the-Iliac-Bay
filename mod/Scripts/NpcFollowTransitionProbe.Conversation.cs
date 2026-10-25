using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static partial class NpcFollowTransitionProbe
    {
        private static readonly BindingFlags ConversationPrivate=BindingFlags.NonPublic|BindingFlags.Instance;
        private const string FollowConversationTurn="It is nice to walk together.";

        private static IEnumerator RecordFollowConversationAfterAcceptance(PeopleOfTheIliacBayMod host,StreetPopulation streets,string residentId,
            Action<string,double,double> capture)
        {
            var job=Store(streets).ActiveNpcAction(residentId);
            if(job==null||job.Kind!=NpcActionKind.Follow||job.FollowTransition!=null)
                throw new InvalidOperationException("Pre-door conversation must begin under the accepted active Follow job.");
            capture(job.Id,job.FinishAt,job.DeadlineAt);
            yield return RecordRealFollowConversation(host,streets,residentId,job.Id,job.FinishAt,job.DeadlineAt);
        }

        private static IEnumerator RecordRealFollowConversation(PeopleOfTheIliacBayMod host,StreetPopulation streets,string residentId,
            string actionId,double finishAt,double deadlineAt)
        {
            var store=Store(streets);var actor=streets.FindActor(residentId);
            if(!actor||!actor.Person||actor.Resident==null||!actor.Resident.IsAlive)
                throw new InvalidOperationException("Follow conversation needs the already-present living fixture resident.");
            var before=store.ActiveNpcAction(residentId);
            if(before==null||NpcActionExecutor.IsTerminal(before)||before.Id!=actionId||before.Kind!=NpcActionKind.Follow||
                before.FinishAt!=finishAt||before.DeadlineAt!=deadlineAt||Now()>=deadlineAt||finishAt>0&&Now()>=finishAt)
                throw new InvalidOperationException("The expected active Follow job must own the pre-door Talk session.");

            var game=GameManager.Instance;
            game.TalkManager.TalkToMobileNPC(actor.Person);
            DaggerfallTalkWindow talk=null;
            yield return WaitForFollowTalk(host,actor,residentId,value=>talk=value);
            var transcript=store.ConversationTranscript(residentId,Now()+2,256);
            foreach(var entry in transcript)
                if(entry.Source!="observed"&&entry.PlayerText==FollowConversationTurn)
                    throw new InvalidOperationException("Pre-door test utterance already exists; refusing to fabricate a new conversation turn.");

            var submit=typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText",ConversationPrivate);
            if(submit==null)throw new InvalidOperationException("Production SubmitFreeText entry point is unavailable.");
            submit.Invoke(host,new object[]{talk,FollowConversationTurn});
            float deadline=Time.realtimeSinceStartup+75;
            NpcMemoryEntry observed=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                foreach(var entry in store.ConversationTranscript(residentId,Now()+2,256))
                    if(entry.Source!="observed"&&entry.PlayerText==FollowConversationTurn){observed=entry;break;}
                if(observed!=null&&!IsConversationBusy(host))break;
                if(DaggerfallUI.UIManager.TopWindow!=talk||!IsConversationOpen(host)||ConversationPersonId(host)!=residentId)
                    throw new InvalidOperationException("Pre-door Talk changed speaker before the real exchange was saved.");
                yield return null;
            }
            if(observed==null||observed.Source!="model"||string.IsNullOrWhiteSpace(observed.NpcText))
                throw new InvalidOperationException("Pre-door exchange was not saved as a completed model reply.");
            if(!VisibleExchange(talk,FollowConversationTurn,observed.NpcText))
                throw new InvalidOperationException("Pre-door model exchange is missing from the native Talk transcript.");
            before=store.ActiveNpcAction(residentId);
            if(before==null||NpcActionExecutor.IsTerminal(before)||before.Id!=actionId||before.FinishAt!=finishAt||
                before.DeadlineAt!=deadlineAt||Now()>=deadlineAt||finishAt>0&&Now()>=finishAt)
                throw new InvalidOperationException("Conversation changed the active Follow owner or consent deadline.");
            CloseFollowTalk(talk);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_CONVERSATION_HISTORY: resident="+residentId+"; action="+actionId+
                "; source=model; visible=true; syntheticPlayerTurn=false");
        }

        private static IEnumerator VerifyOutdoorFollowReentry(PeopleOfTheIliacBayMod host,StreetPopulation streets,string residentId,
            string actionId,double finishAt,double deadlineAt)
        {
            var store=Store(streets);var actor=streets.FindActor(residentId);
            if(!actor||!actor.Person||!actor.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Outdoor Talk requires the existing arrived follower body.");
            var job=store.ActiveNpcAction(residentId);
            if(job==null||NpcActionExecutor.IsTerminal(job)||job.Id!=actionId||job.Kind!=NpcActionKind.Follow||
                job.FollowTransition!=null||job.FinishAt!=finishAt||job.DeadlineAt!=deadlineAt||Now()>=deadlineAt||finishAt>0&&Now()>=finishAt)
                throw new InvalidOperationException("Outdoor Talk must continue the same arrived Follow job without extending consent.");

            GameManager.Instance.TalkManager.TalkToMobileNPC(actor.Person);
            DaggerfallTalkWindow talk=null;
            yield return WaitForFollowTalk(host,actor,residentId,value=>talk=value);
            job=store.ActiveNpcAction(residentId);
            if(job==null||NpcActionExecutor.IsTerminal(job)||job.Id!=actionId||job.FinishAt!=finishAt||
                job.DeadlineAt!=deadlineAt||job.FollowTransition!=null||Now()>=deadlineAt||finishAt>0&&Now()>=finishAt)
                throw new InvalidOperationException("Reopening Talk changed the active Follow job.");
            string context=CurrentFollowContext(host,Now());
            if(string.IsNullOrWhiteSpace(context)||!context.Contains("active Follow action")||
                !context.Contains("do not prove uninterrupted historical presence"))
                throw new InvalidOperationException("Outdoor Talk lacks the grounded active-Follow context.");

            float deadline=Time.realtimeSinceStartup+75;
            while(IsConversationBusy(host)&&Time.realtimeSinceStartup<deadline)
            {
                if(DaggerfallUI.UIManager.TopWindow!=talk||!IsConversationOpen(host)||ConversationPersonId(host)!=residentId)
                    throw new InvalidOperationException("Returning opener changed speaker before it completed.");
                job=store.ActiveNpcAction(residentId);
                if(job==null||NpcActionExecutor.IsTerminal(job)||job.Id!=actionId||job.FinishAt!=finishAt||
                    job.DeadlineAt!=deadlineAt||job.FollowTransition!=null||Now()>=deadlineAt||finishAt>0&&Now()>=finishAt)
                    throw new InvalidOperationException("The original Follow job or consent window ended while the returning opener was pending.");
                yield return null;
            }
            if(IsConversationBusy(host))throw new InvalidOperationException("Returning opener did not complete within 75 real seconds.");
            job=store.ActiveNpcAction(residentId);
            if(job==null||NpcActionExecutor.IsTerminal(job)||job.Id!=actionId||job.FinishAt!=finishAt||
                job.DeadlineAt!=deadlineAt||job.FollowTransition!=null||Now()>=deadlineAt||finishAt>0&&Now()>=finishAt)
                throw new InvalidOperationException("The original Follow job or consent window ended before the returning opener completed.");
            int greetingCount,syntheticTurns;string greeting=ReadVisibleReturningGreeting(talk,out greetingCount,out syntheticTurns);
            if(greetingCount!=1||syntheticTurns!=0||string.IsNullOrWhiteSpace(greeting)||LooksLikeReunion(greeting))
                throw new InvalidOperationException("Outdoor Follow reentry must show one non-reunion NPC opener and no synthetic player turn: count="+
                    greetingCount+"; syntheticTurns="+syntheticTurns+"; greeting="+(greeting??"<missing>"));
            foreach(var entry in store.ConversationTranscript(residentId,Now()+2,256))
                if(entry.Source!="observed"&&entry.PlayerText=="The player returns silently.")
                    throw new InvalidOperationException("Returning greeting fabricated a player exchange in saved conversation history.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_CONVERSATION_REENTRY_PASS: resident="+residentId+"; action="+actionId+
                "; activeFollow=true; context=true; openerCount=1; reunion=false; syntheticPlayerTurn=false; replyCharacters="+greeting.Length);
            CloseFollowTalk(talk);
        }

        private static IEnumerator WaitForFollowTalk(PeopleOfTheIliacBayMod host,ResidentActor actor,string residentId,Action<DaggerfallTalkWindow> ready)
        {
            float deadline=Time.realtimeSinceStartup+25;
            while(!(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)&&Time.realtimeSinceStartup<deadline)yield return null;
            var talk=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            deadline=Time.realtimeSinceStartup+25;
            while((talk==null||!talk.IsSetup||!IsConversationOpen(host)||ConversationPersonId(host)!=residentId||
                GameManager.Instance.TalkManager.CurrentNPCType!=TalkManager.NPCType.Mobile||
                GameManager.Instance.TalkManager.MobileNPC!=actor.Person||IsConversationBusy(host))&&Time.realtimeSinceStartup<deadline)
                yield return null;
            if(talk==null||!talk.IsSetup||!IsConversationOpen(host)||ConversationPersonId(host)!=residentId||
                GameManager.Instance.TalkManager.CurrentNPCType!=TalkManager.NPCType.Mobile||
                GameManager.Instance.TalkManager.MobileNPC!=actor.Person||IsConversationBusy(host))
                throw new InvalidOperationException("Native Talk did not become ready for the exact managed follower.");
            ready(talk);
        }

        private static string CurrentFollowContext(PeopleOfTheIliacBayMod host,double now)
        {
            var method=typeof(PeopleOfTheIliacBayMod).GetMethod("CurrentConversationSharedActivityContext",ConversationPrivate);
            if(method==null)throw new InvalidOperationException("Production shared-activity conversation context is unavailable.");
            return method.Invoke(host,new object[]{now}) as string;
        }

        private static bool VisibleExchange(DaggerfallTalkWindow talk,string question,string answer)
        {
            var field=typeof(PeopleOfTheIliacBayMod).GetField("conversationListField",BindingFlags.Static|BindingFlags.NonPublic);
            var listField=field==null?null:field.GetValue(null) as FieldInfo;
            var transcript=listField==null?null:listField.GetValue(talk) as ListBox;
            if(transcript==null)return false;
            string q=NormalizeFollowText(question),a=NormalizeFollowText(answer);bool found=false;
            for(int i=0;i<transcript.Count;i++)
            {
                var item=transcript.GetItem(i);if(item==null||item.textLabel==null)continue;
                string text=NormalizeFollowText(item.textLabel.Text);
                if(!found){if(item.textColor==DaggerfallUI.DaggerfallQuestionTextColor&&text==q)found=true;continue;}
                return item.textColor!=DaggerfallUI.DaggerfallQuestionTextColor&&text==a;
            }
            return false;
        }

        private static string ReadVisibleReturningGreeting(DaggerfallTalkWindow talk,out int count,out int syntheticTurns)
        {
            count=syntheticTurns=0;string greeting=null;
            var field=typeof(PeopleOfTheIliacBayMod).GetField("conversationListField",BindingFlags.Static|BindingFlags.NonPublic);
            var listField=field==null?null:field.GetValue(null) as FieldInfo;
            var transcript=listField==null?null:listField.GetValue(talk) as ListBox;
            if(transcript==null)return null;
            for(int i=0;i<transcript.Count;i++)
            {
                var item=transcript.GetItem(i);if(item==null||item.textLabel==null)continue;
                string line=(item.textLabel.Text??string.Empty).Trim();
                if(item.textLabel.Name=="PeopleOfTheIliacBayReturnGreeting"){count++;greeting=line;}
                if(item.textColor==DaggerfallUI.DaggerfallQuestionTextColor&&line=="The player returns silently.")syntheticTurns++;
            }
            return greeting;
        }

        private static bool LooksLikeReunion(string value)
        {
            string line=(value??string.Empty).ToLowerInvariant();
            return line.Contains("you're back")||line.Contains("you are back")||line.Contains("returned")||
                line.Contains("reunited")||line.Contains("reunion")||line.Contains("missed you")||
                line.Contains("long time no see")||line.Contains("back together");
        }

        private static string NormalizeFollowText(string value)
        {
            if(string.IsNullOrEmpty(value))return string.Empty;
            var result=new System.Text.StringBuilder(value.Length);bool space=false;
            foreach(char c in value){if(char.IsWhiteSpace(c)){space=result.Length>0;continue;}if(space)result.Append(' ');result.Append(c);space=false;}
            return result.ToString();
        }

        private static void CloseFollowTalk(DaggerfallTalkWindow talk)
        {
            if(DaggerfallUI.UIManager.TopWindow==talk)DaggerfallUI.Instance.PopToHUD();
        }

        private static bool IsConversationOpen(PeopleOfTheIliacBayMod host)
        {
            var field=typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen",ConversationPrivate);
            return field!=null&&(bool)field.GetValue(host);
        }
        private static string ConversationPersonId(PeopleOfTheIliacBayMod host)
        {
            var field=typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId",ConversationPrivate);
            return field==null?null:field.GetValue(host) as string;
        }
        private static bool IsConversationBusy(PeopleOfTheIliacBayMod host)
        {
            var field=typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy",ConversationPrivate);
            return field!=null&&(bool)field.GetValue(host);
        }
    }
}
