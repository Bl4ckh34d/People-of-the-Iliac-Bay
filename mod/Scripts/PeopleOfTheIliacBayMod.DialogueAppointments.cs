using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private bool TryAppointmentConversation(DaggerfallTalkWindow window,string text,string history,
            ConversationLocationSnapshot submittedLocation=null,ConversationContextSnapshot snapshot=null)
        {
            if(snapshot!=null)
            {
                submittedLocation=snapshot.PlayerLocationAtSubmission;
                if(!IsCurrentConversationContext(snapshot,window))
                {HandleRejectedConversationStage(snapshot,window,0,text,"No appointment was booked or cancelled.");return true;}
            }
            var actor=conversationActor;
            if(!actor || actor.Resident==null || actor.Resident.IsQuestProtected || actor.Resident.AgeGroup!=ResidentAgeGroup.Adult)
            {
                conversationResponseCue+=" No appointment was booked: no controllable adult schedule is available. Do not promise to attend a meeting.";
                SubmitSocialText(window,text,history,snapshot:snapshot); return true;
            }
            var places=ConversationAppointmentPlaces(actor.Resident.Id);
            var appointments=store.AppointmentsFor(actor.Resident.Id);
            appointments.RemoveAll(a=>a.OutcomeRecorded);
            double requestedAt=snapshot!=null?snapshot.CapturedGameTime:
                submittedLocation==null?DaggerfallUnity.Instance.WorldTime.Now.ToSeconds():submittedLocation.CapturedAt;
            int minute=(int)(requestedAt%86400/60);
            var requestedDate=new DaggerfallDateTime(); requestedDate.FromSeconds((ulong)requestedAt);
            var calendar=new AppointmentCalendar(requestedDate.Year,requestedDate.MonthOfYear,requestedDate.DayOfMonth);
            string context=snapshot==null?
                CurrentConversationClock()+" "+ConversationPlaceContext()+" "+
                    CurrentConversationLifeContext(actor.Person.NameNPC,CurrentDialogueDisposition())+" "+
                    AppointmentPatienceConversationContext()+" "+ConversationAppointmentContext():
                ConversationContextFormatter.ForSemanticClassifier(snapshot,2048)+" "+
                    (submittedLocation==null?"Player HERE at submission is unknown. Do not infer a current location or use a later position.":string.Empty)+" "+
                    AppointmentPatienceConversationContext()+" "+ConversationAppointmentContext();
            var game=GameManager.Instance;
            if(snapshot==null && submittedLocation==null && game.PlayerEnterExit.IsPlayerInsideBuilding)
                context+=" Actual current buildingkey="+game.PlayerEnterExit.BuildingDiscoveryData.buildingKey+". 'Here' refers only to this building.";
            for(int i=0;i<appointments.Count && i<100;i++)
                context+=" Cancellation index "+i+": "+DescribeAppointment(appointments[i])+".";
            int session=memorySession;string person=actor.Resident.Id;
            string prompt=SemanticAppointmentChat.Prompt(text,history,context,places,minute,calendar,appointments.Count,submittedLocation);
            string grammar=SemanticAppointmentChat.GrammarFor(places,appointments.Count,submittedLocation);
            long stageToken=snapshot==null?0:BeginConversationContextStage(snapshot,window);
            if(snapshot!=null && stageToken<=0)
            {HandleRejectedConversationStage(snapshot,window,stageToken,text,"No appointment was booked or cancelled.");return true;}
            if(!localDialogue.Classify(NewDialogueConnection(),prompt,history,output=>
            {
                if(!CurrentMemorySession(window,session) || conversationPersonId!=person ||
                    snapshot!=null && !TryConsumeConversationContextStage(snapshot,window,stageToken))
                {if(snapshot!=null)HandleRejectedConversationStage(snapshot,window,stageToken,text,"No appointment was booked or cancelled.");return;}
                if(output==LocalDialogueClient.Unavailable || output==LocalDialogueClient.ContextLimit)
                { conversationBusy=false; AppendRememberedDialoguePair(window,text,DialogueReplyFallback.UnavailableReply,"unavailable"); DaggerfallUI.AddHUDText("Local dialogue unavailable. No appointment was booked."); return; }
                var decision=SemanticAppointmentChat.Parse(output,places,minute,appointments.Count,submittedLocation);
                if(snapshot!=null && decision!=null && decision.Kind=="BOOK" && decision.Place==null)
                {
                    if(submittedLocation==null)
                        decision=new AppointmentChatDecision {Kind="CLARIFY"};
                    else
                        decision.Place=new AppointmentPlace {Kind="building",
                            SettlementId=submittedLocation.PlaceCopy().SettlementId,BuildingKey=decision.BuildingKey};
                }
                string fallback;
                string result=ApplyAppointmentDecision(decision,appointments,person,out fallback,requestedAt);
                conversationResponseCue+=" "+result;
                SubmitSocialText(window,text,history,knowledgeResult:result,verifiedFallback:fallback,snapshot:snapshot);
            },grammar))
            {
                if(snapshot!=null && !TryConsumeConversationContextStage(snapshot,window,stageToken))
                {HandleRejectedConversationStage(snapshot,window,stageToken,text,"No appointment was booked or cancelled.");return true;}
                conversationBusy=false; AppendRememberedDialoguePair(window,text,DialogueReplyFallback.UnavailableReply,"unavailable"); DaggerfallUI.AddHUDText("Waiting for the current reply. No appointment was booked.");
            }
            return true;
        }

        private List<ConversationTopic> ConversationAppointmentPlaces(string residentId)
        {
            var places=new List<ConversationTopic>();
            if(!streets || !streets.Ready) return places;
            foreach(int key in streets.AppointmentVenueKeys(residentId))
            {
                string name=ConversationBuildingName(key);
                if(!string.IsNullOrWhiteSpace(name)) places.Add(new ConversationTopic(key,name,"Verified places in your local walking routine",false));
            }
            return places;
        }

        private string ApplyAppointmentDecision(AppointmentChatDecision decision,List<AppointmentRecord> pending,string residentId,out string fallback,double requestedAt=-1)
        {
            fallback="No appointment was booked or cancelled.";
            const string noBooking="No new appointment was booked. Do not promise future attendance, reserved plans, or completed actions.";
            if(decision==null || decision.Kind=="NONE") return noBooking+" Continue naturally; distinguish a casual wish from a committed plan.";
            if(decision.Kind=="DECLINE") return noBooking+" You chose to decline this invitation. Explain briefly in your own voice.";
            if(decision.Kind=="CLARIFY") return noBooking+" Ask one natural question to resolve the missing day, exact time, venue or cancellation target.";
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            if(decision.Kind=="CANCEL")
            {
                string reason;
                if(decision.CancelIndex<0 || decision.CancelIndex>=pending.Count ||
                    !streets.TryCancelAppointment(pending[decision.CancelIndex].Id,now,out reason))
                    return noBooking+" The proposed cancellation could not be applied. Do not claim a booking was cancelled.";
                fallback="The appointment was cancelled: "+DescribeAppointment(pending[decision.CancelIndex])+".";
                return "APPOINTMENT CANCELLED: "+DescribeAppointment(pending[decision.CancelIndex])+". Acknowledge the actual cancellation naturally.";
            }
            if(decision.Kind!="BOOK") return noBooking;
            int town=decision.Place!=null?decision.Place.SettlementId:
                GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId;
            double starts=Math.Floor((requestedAt<0 ? now : requestedAt)/86400)*86400+decision.DayOffset*86400d+decision.MinuteOfDay*60d;
            string appointmentId="appointment:"+Guid.NewGuid().ToString("N");
            int actualDisposition=CaptureAppointmentDisposition(residentId);
            int patienceSeconds=AppointmentTiming.CreatePatienceSeconds(store.WorldSeed,residentId,appointmentId,actualDisposition);
            var appointment=new AppointmentRecord { Id=appointmentId, ResidentId=residentId,
                Purpose="social meeting", Place=decision.Place??new AppointmentPlace {Kind="building",SettlementId=town,
                    BuildingKey=decision.BuildingKey,Detail=ConversationBuildingName(decision.BuildingKey)},
                StartsAt=starts,EndsAt=starts+decision.DurationMinutes*60d,GraceSeconds=15*60,
                PatienceSeconds=patienceSeconds,Importance=decision.Importance };
            string failure;
            if(!streets.TryBookAppointment(appointment,out failure))
                return noBooking+" Scheduler reports: "+failure+". Decline or suggest asking for another time; do not pretend the meeting is confirmed.";
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_PATIENCE_CAPTURED: "+appointment.Id+"; resident="+residentId+
                "; actualDisposition="+actualDisposition+"; patienceSeconds="+patienceSeconds+"; capturedAtBooking=true");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_RESERVED: "+appointment.Id+"; "+residentId+"; "+DescribeAppointment(appointment));
            fallback="The appointment is confirmed: "+DescribeAppointment(appointment)+".";
            return "APPOINTMENT CONFIRMED AND SAVED: "+DescribeAppointment(appointment)+
                ". You agreed and the scheduler has reserved this slot including real walking time. Confirm these exact details in character, without mentioning software or schedules as implementation.";
        }

        private int CaptureAppointmentDisposition(string residentId)
        {
            var manager=GameManager.Instance==null?null:GameManager.Instance.TalkManager;
            if(manager!=null && reactionToPlayerField!=null)
            {
                object live=reactionToPlayerField.GetValue(manager);
                if(live is int) return Mathf.Clamp((int)live,-100,100);
            }
            int saved;
            if(store!=null && store.TryGetActualConversationDisposition(residentId,out saved))
                return Mathf.Clamp(saved,-100,100);
            return 0;
        }

        private static string AppointmentPatienceConversationContext()
        {
            return "If you accept a new meeting, you will wait 10–150 minutes after the agreed start if the player is late; your personal disposition when you accept determines the exact limit. "+
                "The meeting's duration is separate. State an exact limit naturally only when confirmed details give it; otherwise do not guess. Never mention hidden rules or game mechanics.";
        }

        private string ConversationAppointmentContext()
        {
            if(store==null || string.IsNullOrEmpty(conversationPersonId)) return "No verified appointment schedule is available.";
            var appointments=store.AppointmentsFor(conversationPersonId);
            string result="Only meetings we have actually agreed to are commitments. Never claim we made plans merely because you suggested them. ";
            int shown=0;
            for(int i=appointments.Count-1;i>=0 && shown<6;i--,shown++)
                result+=" Agreed meeting: "+DescribeAppointment(appointments[i])+"; current situation="+appointments[i].Status+".";
            if(shown==0) result+="No appointments have been confirmed.";
            return result;
        }

        private static string DescribeAppointment(AppointmentRecord appointment)
        {
            var date=new DaggerfallDateTime(); date.FromSeconds((ulong)appointment.StartsAt);
            string result=appointment.Purpose+" at "+appointment.Place.Detail+", "+date.DateString()+", "+date.Year+" at "+
                date.MinTimeString()+" for "+((appointment.EndsAt-appointment.StartsAt)/60).ToString("0",CultureInfo.InvariantCulture)+" minutes";
            if(appointment.PatienceSeconds>0)
                result+="; I can wait up to "+(appointment.PatienceSeconds/60d).ToString("0.##",CultureInfo.InvariantCulture)+
                    " minutes after our agreed start if you are late";
            return result;
        }
    }
}
