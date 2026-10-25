using System;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private string pendingContactId;
        private string pendingContactResidentId;
        private string openedContactId;
        private string openedContactResidentId;

        // Called by the bounded physical-arrival observer. False means a live
        // safety gate or the native window is not ready; the observer may retry.
        internal bool TryInitiateProactiveContactConversation(string residentId,string contactId)
        {
            if(string.IsNullOrWhiteSpace(residentId)||string.IsNullOrWhiteSpace(contactId)) return false;
            var currentTalk=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            var actor=streets?streets.FindActor(residentId):null;
            if(openedContactId==contactId&&openedContactResidentId==residentId&&currentTalk!=null&&actor&&
                GameManager.Instance!=null&&GameManager.Instance.TalkManager.CurrentNPCType==TalkManager.NPCType.Mobile&&
                GameManager.Instance.TalkManager.MobileNPC==actor.Person) return true;
            if(!string.IsNullOrEmpty(pendingContactId))
            {
                if(pendingContactId!=contactId||pendingContactResidentId!=residentId) return false;
                return currentTalk!=null&&TryOpenPendingContact(currentTalk);
            }
            if(!CanInitiateContact(residentId,contactId,out actor,out _)) return false;

            pendingContactId=contactId;
            pendingContactResidentId=residentId;
            GameManager.Instance.TalkManager.TalkToMobileNPC(actor.Person);
            var talk=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            if(talk!=null) return TryOpenPendingContact(talk);
            return false;
        }

        // Compatibility for the route adapter until its typed contact call is deployed.
        internal bool TryInitiateAppointmentConversation(string residentId,string appointmentId)
        { return TryInitiateProactiveContactConversation(residentId,"meeting:"+appointmentId); }

        private bool CanInitiateContact(string residentId,string contactId,out ResidentActor actor,out ProactiveContactOffer offer)
        {
            actor=null;offer=default;
            var game=GameManager.Instance;
            if(saveFault||!options.Enabled||store==null||!streets||!streets.Ready||game==null||!game.IsReady||
                !game.IsPlayingGame()||game.SaveLoadManager.LoadInProgress||GameManager.IsGamePaused||
                !game.IsPlayerOnHUD||game.PlayerEntity.IsInvisible||game.PlayerEntity.IsMagicallyConcealed||
                game.AreEnemiesNearby(false,false)||conversationSessionOpen||conversationBusy||
                DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow) return false;
            actor=streets.FindActor(residentId);
            if(!actor||!actor.gameObject.activeInHierarchy||actor.Person==null||actor.Resident==null||
                !actor.Resident.IsAlive||actor.Resident.AgeGroup!=ResidentAgeGroup.Adult||actor.Resident.IsQuestProtected||
                actor.IsSleeping||actor.IsConversing) return false;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            if(!store.TryGetProactiveContactOffer(residentId,now,out offer)||offer.ContactId!=contactId||
                offer.NoticedAt<0||offer.ApproachUntil<now) return false;
            if(offer.Kind==ProactiveContactKind.CompanionHealthRequest &&
                (!streets.TryObservedCompanionHealthCondition(actor,now,out string condition)||condition!=offer.Purpose))return false;
            if(!TryFormatContact(offer,now,out _,out _)) return false;
            if(!CanStartMobileConversation(actor.Person)) return false;
            Vector3 player=game.PlayerMotor.transform.position;
            float distance=offer.Kind==ProactiveContactKind.CompanionHealthRequest?
                StreetPopulation.CompanionHealthTalkDistance:1.35f;
            return Vector3.Distance(actor.transform.position,player)<=distance&&
                CivilianResponses.Sees(actor.transform.position+Vector3.up,player+Vector3.up,
                    offer.Kind==ProactiveContactKind.CompanionHealthRequest?distance:2f);
        }

        private bool TryOpenPendingContact(DaggerfallTalkWindow talk)
        {
            if(string.IsNullOrEmpty(pendingContactId)||talk==null||!talk.IsSetup||
                DaggerfallUI.UIManager.TopWindow!=talk) return false;
            var manager=GameManager.Instance==null?null:GameManager.Instance.TalkManager;
            var actor=streets?streets.FindActor(pendingContactResidentId):null;
            if(manager==null||manager.CurrentNPCType!=TalkManager.NPCType.Mobile||!actor||
                manager.MobileNPC!=actor.Person||actor.Resident==null||actor.Resident.Id!=pendingContactResidentId||
                actor.Resident.IsQuestProtected||!actor.Resident.IsAlive||actor.IsSleeping)
            { ClearPendingContact("native-speaker-or-actor-mismatch"); return false; }

            var game=GameManager.Instance;
            if(game==null||!game.IsReady||DaggerfallUI.UIManager.TopWindow!=talk||
                !DaggerfallUI.UIManager.ContainsWindow(talk)||game.SaveLoadManager.LoadInProgress||
                game.PlayerEntity.IsInvisible||game.PlayerEntity.IsMagicallyConcealed||
                game.AreEnemiesNearby(false,false)||!CanStartMobileConversation(actor.Person))
            { ClearPendingContact("talk-window-state-or-safety-gate"); return false; }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_CONTACT_WINDOW_GATE_PASS: contact="+pendingContactId+
                "; exactTalkWindow=true; nativeTalkWindowPaused="+GameManager.IsGamePaused+
                "; isPlayingGame="+game.IsPlayingGame()+"; loading="+game.SaveLoadManager.LoadInProgress);
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            if(!store.TryGetProactiveContactOffer(pendingContactResidentId,now,out var offer)||
                offer.ContactId!=pendingContactId||offer.ApproachUntil<now)
            { ClearPendingContact("contact-offer-expired-or-mismatched"); return false; }
            if(offer.Kind==ProactiveContactKind.CompanionHealthRequest &&
                (!streets.TryObservedCompanionHealthCondition(actor,now,out string condition)||condition!=offer.Purpose))
            { ClearPendingContact("companion-condition-or-binding-changed"); return false; }
            Vector3 player=game.PlayerMotor.transform.position;
            float distance=offer.Kind==ProactiveContactKind.CompanionHealthRequest?
                StreetPopulation.CompanionHealthTalkDistance:1.35f;
            bool near=Vector3.Distance(actor.transform.position,player)<=distance;
            bool visible=near&&CivilianResponses.Sees(actor.transform.position+Vector3.up,player+Vector3.up,
                offer.Kind==ProactiveContactKind.CompanionHealthRequest?distance:2f);
            if(!near||!visible) { ClearPendingContact("native-proximity-or-visibility-lost"); return false; }
            if(!TryFormatContact(offer,now,out _,out _)) { ClearPendingContact("contact-context-unavailable"); return false; }
            bool confirmed=store.TryClaimProactiveContactConversation(offer.ContactId,offer.Kind,
                pendingContactResidentId,actor.Resident.Id,now,true,true,true,true,out var receipt,out bool isNew);
            if(!confirmed||!isNew) { ClearPendingContact("core-open-receipt-rejected"); return false; }

            // Remove only the native opening line. BeginDialogueMemory restores
            // the saved history; the new opener adds no fabricated player turn.
            var transcript=conversationListField==null?null:conversationListField.GetValue(talk) as ListBox;
            if(transcript!=null) transcript.ClearItems();
            BeginGroundedConversation();
            returningGreetingPending=false;
            openedContactId=receipt.ContactId;
            openedContactResidentId=receipt.ResidentId;
            ClearPendingContact("receipt-committed");

            if(!TryFormatContact(receipt,now,out string promptContext,out string fallback))
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_CONTACT_CONTEXT_MISSING: contact="+receipt.ContactId);
                fallback="There you are. Good to see you.";
                promptContext="A resident has opened a conversation. Greet the player briefly without inventing shared history or plans.";
            }
            // Unlike RequestReturningGreeting(), this path starts directly from
            // the proactive Talk-window handoff. Mark the asynchronous reply
            // busy before dispatch so input stays locked until its visible line
            // (or verified fallback) has actually been appended.
            conversationBusy=true;
            SubmitSocialText(talk,"A proactive contact has opened this conversation.",ReadConversationHistory(talk),
                knowledgeResult:promptContext,
                returningGreeting:true,verifiedFallback:fallback);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_CONTACT_OPENED: contact="+receipt.ContactId+
                "; resident="+receipt.ResidentId+"; kind="+receipt.Kind+"; newlyOpened=true; actualTalkWindow=true");
            return true;
        }

        private bool TryFormatContact(ProactiveContactOffer offer,double now,out string promptContext,out string fallback)
        {
            return TryFormatContact(offer.Kind,offer.AppointmentId,offer.Purpose,offer.Place,offer.StartsAt,offer.EndsAt,
                offer.ResolvedAt,offer.NpcWaitedSeconds,offer.PatienceSeconds,offer.ActualDisposition,
                offer.GroundedPlayerFact,offer.GroundedNpcFact,offer.PlayerAtVenueAt,now,out promptContext,out fallback);
        }

        private bool TryFormatContact(ProactiveContactReceipt receipt,double now,out string promptContext,out string fallback)
        {
            return TryFormatContact(receipt.Kind,receipt.AppointmentId,receipt.Purpose,receipt.Place,receipt.StartsAt,receipt.EndsAt,
                receipt.ResolvedAt,receipt.NpcWaitedSeconds,receipt.PatienceSeconds,receipt.ActualDisposition,
                receipt.GroundedPlayerFact,receipt.GroundedNpcFact,receipt.PlayerAtVenueAt,now,out promptContext,out fallback);
        }

        private bool TryFormatContact(ProactiveContactKind kind,string appointmentId,string purpose,AppointmentPlace place,
            double startsAt,double endsAt,double resolvedAt,double npcWaitedSeconds,int patienceSeconds,int disposition,
            string groundedPlayerFact,string groundedNpcFact,double playerAtVenueAt,double now,
            out string promptContext,out string fallback)
        {
            promptContext="";fallback="";
            if(kind==ProactiveContactKind.AppointmentMeeting)
            {
                string placeName=place==null?null:place.Detail;
                var when=new DaggerfallDateTime();when.FromSeconds((ulong)startsAt);
                string agreed=when.DateString()+", "+when.Year+" at "+when.MinTimeString()+" for "+
                    ((endsAt-startsAt)/60d).ToString("0.##",CultureInfo.InvariantCulture)+" minutes";
                promptContext=AppointmentArrivalConversationContext.Format(purpose,placeName,agreed,
                    patienceSeconds/60d,playerAtVenueAt>startsAt+1&&npcWaitedSeconds>1);
                fallback=AppointmentArrivalConversationContext.FallbackReply(purpose,placeName,agreed);
                return !string.IsNullOrWhiteSpace(promptContext)&&!string.IsNullOrWhiteSpace(fallback);
            }
            if(kind==ProactiveContactKind.MissedAppointmentFollowUp)
            {
                var appointment=string.IsNullOrEmpty(appointmentId)?null:store.FindAppointment(appointmentId);
                if(appointment==null||appointment.Status!="player-absent"||!appointment.OutcomeRecorded||resolvedAt<0||resolvedAt>now) return false;
                string placeName=place==null?null:place.Detail;
                var when=new DaggerfallDateTime();when.FromSeconds((ulong)startsAt);
                string scheduled=when.DateString()+", "+when.Year+" at "+when.MinTimeString();
                string mood=ProactiveContactConversationContext.MissedMood(disposition);
                promptContext=ProactiveContactConversationContext.FormatMissedAppointment(purpose,placeName,scheduled,
                    npcWaitedSeconds,now-resolvedAt,mood);
                fallback=ProactiveContactConversationContext.MissedAppointmentFallback(purpose,placeName,npcWaitedSeconds,mood);
                return !string.IsNullOrWhiteSpace(promptContext)&&!string.IsNullOrWhiteSpace(fallback);
            }
            if(kind==ProactiveContactKind.FamiliarGreeting)
            {
                promptContext=ProactiveContactConversationContext.FormatFamiliarGreeting(groundedPlayerFact,groundedNpcFact);
                fallback=ProactiveContactConversationContext.FamiliarGreetingFallback(groundedPlayerFact);
                return !string.IsNullOrWhiteSpace(promptContext)&&
                    !string.IsNullOrWhiteSpace(groundedPlayerFact)&&!string.IsNullOrWhiteSpace(groundedNpcFact);
            }
            if(kind==ProactiveContactKind.CompanionHealthRequest)
            {
                promptContext=ProactiveContactConversationContext.FormatCompanionHealthRequest(purpose);
                fallback=ProactiveContactConversationContext.CompanionHealthRequestFallback(purpose);
                return !string.IsNullOrWhiteSpace(promptContext)&&!string.IsNullOrWhiteSpace(fallback);
            }
            return false;
        }

        private bool IsPendingProactiveContactFor(ResidentActor actor)
        { return actor&&actor.Resident!=null&&!string.IsNullOrEmpty(pendingContactId)&&pendingContactResidentId==actor.Resident.Id; }

        private void ClearPendingContact(string reason="unspecified")
        {
            if(!string.IsNullOrEmpty(pendingContactId))
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_CONTACT_PENDING_CLEAR: contact="+pendingContactId+
                    "; resident="+pendingContactResidentId+"; reason="+reason+
                    "; topIsTalk="+(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)+
                    "; containsTalk="+DaggerfallUI.UIManager.ContainsWindow(DaggerfallUI.Instance.TalkWindow)+
                    "; paused="+GameManager.IsGamePaused);
            pendingContactId=null;pendingContactResidentId=null;
        }

        private void ClearOpenedContact()
        { openedContactId=null;openedContactResidentId=null; }
    }
}
