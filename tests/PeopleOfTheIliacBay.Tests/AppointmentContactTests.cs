using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class AppointmentContactTests
    {
        private static WorldStateStore Create(out string resident)
        {
            var store=new WorldStateStore(71);
            resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0].Id;
            var booking=new AppointmentRecord {Id="meeting-contact",ResidentId=resident,Purpose="social meeting",
                StartsAt=1000,EndsAt=1060,GraceSeconds=0,PatienceSeconds=600,Importance=2,Status="accepted",
                Place=new AppointmentPlace{Kind="building",SettlementId=100,BuildingKey=20,Detail="The Blue Room"}};
            if(!store.TryReserveAppointment(booking,500,out var reason))throw new InvalidOperationException(reason);
            return store;
        }

        private static WorldStateStore CreateLongWindowMeeting(out string resident)
        {
            var store=new WorldStateStore(71);
            resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0].Id;
            var booking=new AppointmentRecord {Id="long-window",ResidentId=resident,Purpose="social meeting",
                StartsAt=1000,EndsAt=1900,GraceSeconds=0,PatienceSeconds=600,Importance=2,Status="accepted",
                Place=new AppointmentPlace{Kind="building",SettlementId=100,BuildingKey=20,Detail="The Blue Room"}};
            if(!store.TryReserveAppointment(booking,500,out var reason))throw new InvalidOperationException(reason);
            return store;
        }

        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Appointment player sighting creates bounded approach grace without attendance",new Action(()=>{
                var store=Create(out var resident);string id="meeting-contact";
                store.ObserveAppointment(id,1000,true,false,false);
                Check.True(store.TryNoticeAppointmentPlayer(id,1580,1900));
                var noticed=store.FindAppointment(id);
                Check.Near(1580,noticed.PlayerNoticedAt);Check.Near(1700,noticed.ApproachUntil);
                Check.Near(1700,AppointmentTiming.ResolutionDeadline(noticed));
                Check.Near(1700,AppointmentTiming.ReservedUntil(noticed));
                Check.Equal(-1d,noticed.PlayerArrivedAt);Check.Equal("waiting",noticed.Status);
                Check.True(!noticed.OutcomeRecorded);
                Check.True(store.TryNoticeAppointmentPlayer(id,1600,2000));
                Check.Near(1580,store.FindAppointment(id).PlayerNoticedAt);
                Check.Near(1700,store.FindAppointment(id).ApproachUntil);
            })));

            cases.Add(("Appointment approach grace clamps to safe time and blocks later reservation",new Action(()=>{
                var store=Create(out var resident);string id="meeting-contact";
                store.ObserveAppointment(id,1000,true,false,false);
                Check.True(store.TryNoticeAppointmentPlayer(id,1580,1640));
                var noticed=store.FindAppointment(id);
                Check.Near(1640,noticed.ApproachUntil);
                Check.Near(1640,AppointmentTiming.ReservedUntil(noticed));
                var overlap=new AppointmentRecord{Id="next-meeting",ResidentId=resident,Purpose="next",
                    StartsAt=1639,EndsAt=1680,GraceSeconds=0,PatienceSeconds=600,Importance=1,Status="accepted",
                    Place=new AppointmentPlace{Kind="building",SettlementId=100,BuildingKey=21}};
                Check.True(!store.TryReserveAppointment(overlap,900,out var reason));
                Check.Equal("overlap",reason);
                Check.True(store.TryNoticeAppointmentPlayer(id,1620,1620));
                var noGrace=Create(out _);noGrace.ObserveAppointment(id,1000,true,false,false);
                Check.True(!noGrace.TryNoticeAppointmentPlayer(id,1580,1580));
                Check.Near(1580,noGrace.FindAppointment(id).PlayerNoticedAt);
                Check.Near(1580,noGrace.FindAppointment(id).ApproachUntil);

                var clamped=Create(out var sameResident);clamped.ObserveAppointment(id,1000,true,false,false);
                var next=new AppointmentRecord{Id="next-meeting",ResidentId=sameResident,Purpose="next",
                    StartsAt=1650,EndsAt=1710,GraceSeconds=0,PatienceSeconds=600,Importance=1,Status="accepted",
                    Place=new AppointmentPlace{Kind="building",SettlementId=100,BuildingKey=21}};
                Check.True(clamped.TryReserveAppointment(next,900,out _));
                Check.True(clamped.TryNoticeAppointmentPlayer(id,1580,1900));
                Check.Near(1650,clamped.FindAppointment(id).ApproachUntil);
            })));

            cases.Add(("Appointment sighting delays resolution only while observed wait remains valid",new Action(()=>{
                var store=Create(out _);string id="meeting-contact";
                for(double at=1000;at<=1580;at+=10)
                {
                    store.ObserveAppointment(id,at,true,false,false);
                    if(at==1580)Check.True(store.TryNoticeAppointmentPlayer(id,at,1700));
                }
                store.ObserveAppointment(id,1601,true,false,false);
                Check.True(!store.FindAppointment(id).OutcomeRecorded);
                for(double at=1610;at<=1700;at+=10)store.ObserveAppointment(id,at,true,false,false);
                store.ObserveAppointment(id,1701,false,false,false);
                Check.Equal("player-absent",store.FindAppointment(id).Status);
                Check.True(store.FindAppointment(id).NoShowPenalty>0);

                var interrupted=Create(out _);id="meeting-contact";
                interrupted.ObserveAppointment(id,1000,true,false,false);
                Check.True(interrupted.TryNoticeAppointmentPlayer(id,1580,1700));
                interrupted.ObserveAppointment(id,1581,false,false,false);
                interrupted.ObserveAppointment(id,1701,false,false,false);
                Check.Equal("interrupted",interrupted.FindAppointment(id).Status);
                Check.Equal(0,interrupted.FindAppointment(id).NoShowPenalty);

                var venueOnly=Create(out _);id="meeting-contact";
                for(double at=1000;at<=1600;at+=10)
                    venueOnly.ObserveAppointment(id,at,true,false,at==1000,false);
                venueOnly.ObserveAppointment(id,1601,true,false,false,false);
                Check.Equal("interrupted",venueOnly.FindAppointment(id).Status);
                Check.Equal(0,venueOnly.FindAppointment(id).NoShowPenalty);
                Check.Near(1000,venueOnly.FindAppointment(id).PlayerAtVenueAt);
            })));

            cases.Add(("Appointment opener claim needs exact open nearby speaker and survives restore once",new Action(()=>{
                var store=Create(out var resident);string id="meeting-contact";
                store.ObserveAppointment(id,1000,true,false,false);
                Check.True(store.TryGetAppointmentMeetingOffer(resident,1010,out var offer));
                Check.Equal(id,offer.AppointmentId);Check.Equal(resident,offer.ResidentId);
                Check.Equal("The Blue Room",offer.Place.Detail);Check.Equal(600,offer.PatienceSeconds);
                Check.True(!store.TryClaimAppointmentConversation(id,resident,"wrong",1011,true,true,true,true,out _,out _));
                Check.True(!store.TryClaimAppointmentConversation(id,resident,resident,1011,false,true,true,true,out _,out _));
                Check.True(!store.TryClaimAppointmentConversation(id,resident,resident,1011,true,false,true,true,out _,out _));
                Check.True(!store.TryClaimAppointmentConversation(id,resident,resident,1011,true,true,false,true,out _,out _));
                Check.True(!store.TryClaimAppointmentConversation(id,resident,resident,1011,true,true,true,false,out _,out _));
                Check.Equal(-1d,store.FindAppointment(id).ConversationOpenedAt);
                Check.Equal(-1d,store.FindAppointment(id).PlayerArrivedAt);

                store.ObserveAppointment(id,1010,true,true,false);
                Check.Equal("attended",store.FindAppointment(id).Status);
                Check.True(store.TryGetAppointmentMeetingOffer(resident,1011,out offer));
                Check.True(store.TryClaimAppointmentConversation(id,resident,resident,1011,true,true,true,true,out var receipt,out var isNew));
                Check.True(isNew);Check.Equal(id,receipt.AppointmentId);Check.Equal("The Blue Room",receipt.Place.Detail);
                Check.Near(1000,receipt.StartsAt);Check.Near(1011,receipt.ConversationOpenedAt);Check.Equal(600,receipt.PatienceSeconds);
                Check.Near(1011,store.FindAppointment(id).ConversationOpenedAt);
                Check.True(!store.TryGetAppointmentMeetingOffer(resident,1012,out _));

                var json=JsonSerializer.Serialize(store.Snapshot(),new JsonSerializerOptions{IncludeFields=true});
                var restored=new WorldStateStore(99);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(json,new JsonSerializerOptions{IncludeFields=true}));
                Check.True(restored.TryClaimAppointmentConversation(id,resident,resident,1012,true,true,true,true,out var duplicate,out isNew));
                Check.True(!isNew);Check.Equal(receipt.AppointmentId,duplicate.AppointmentId);
                Check.Near(1011,duplicate.ConversationOpenedAt);
                Check.Near(1010,restored.FindAppointment(id).PlayerArrivedAt);
            })));

            cases.Add(("Typed meeting contact claims only the exact opened speaker and persists its receipt",new Action(()=>{
                var store=Create(out var resident);store.ObserveAppointment("meeting-contact",1000,true,false,false);
                Check.True(store.TryGetProactiveContactOffer(resident,1010,out var offer));
                Check.Equal(ProactiveContactKind.AppointmentMeeting,offer.Kind);
                Check.Equal("meeting:meeting-contact",offer.ContactId);Check.Equal(1060d,offer.EndsAt);
                Check.True(store.TryNoticeProactiveContactPlayer(offer,1011,1120));
                Check.True(!store.TryClaimProactiveContactConversation(offer.ContactId,offer.Kind,resident,
                    "wrong-speaker",1012,true,true,true,true,out _,out _));
                Check.True(store.TryClaimProactiveContactConversation(offer.ContactId,offer.Kind,resident,
                    resident,1012,true,true,true,true,out var receipt,out var isNew));
                Check.True(isNew);Check.Equal(offer.ContactId,receipt.ContactId);Check.Equal(1012d,receipt.OpenedAt);
                Check.Equal("The Blue Room",receipt.Place.Detail);
                var json=JsonSerializer.Serialize(store.Snapshot(),new JsonSerializerOptions{IncludeFields=true});
                var restored=new WorldStateStore(9);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(json,new JsonSerializerOptions{IncludeFields=true}));
                Check.True(restored.TryClaimProactiveContactConversation(offer.ContactId,offer.Kind,resident,
                    resident,1013,true,true,true,true,out var duplicate,out isNew));
                Check.True(!isNew);Check.Near(1012,duplicate.OpenedAt);
            })));

            cases.Add(("Attended but unopened typed meeting can still create its proactive opener",new Action(()=>{
                var store=CreateLongWindowMeeting(out var resident);string id="long-window";
                store.ObserveAppointment(id,1000,true,false,false);
                // The physical-presence observer runs before the proactive contact
                // observer in the same runtime tick, so attendance may be durable
                // before the opener has had a chance to latch its approach.
                store.ObserveAppointment(id,1500,true,true,false);
                var attended=store.FindAppointment(id);
                Check.Equal("attended",attended.Status);Check.True(attended.OutcomeRecorded);
                Check.Equal(-1d,attended.ConversationOpenedAt);
                // Arrival patience ended at 1600, but the NPC and player actually
                // attended before that; the agreed meeting remains open to 1900.
                Check.True(store.TryGetProactiveContactOffer(resident,1650,out var offer));
                Check.Equal(ProactiveContactKind.AppointmentMeeting,offer.Kind);
                Check.True(store.TryNoticeProactiveContactPlayer(offer,1650,1750));
                Check.True(store.TryClaimProactiveContactConversation(offer.ContactId,offer.Kind,resident,resident,
                    1655,true,true,true,true,out var receipt,out var isNew));
                Check.True(isNew);Check.Near(1655,receipt.OpenedAt);
                Check.Equal("attended",store.FindAppointment(id).Status);
                Check.Equal(0,store.FindAppointment(id).NoShowPenalty);
                Check.True(!store.TryGetProactiveContactOffer(resident,1901,out _));
                Check.True(!store.TryNoticeAppointmentPlayer(id,1901,2000));
            })));

            cases.Add(("Interrupted missed-appointment approach is neutral and can be offered on a later day",new Action(()=>{
                var store=Create(out var resident);string id="meeting-contact";
                for(double at=1000;at<=1600;at+=10)store.ObserveAppointment(id,at,true,false,false);
                store.ObserveAppointment(id,1601,false,false,false);
                Check.Equal("player-absent",store.FindAppointment(id).Status);
                int originalPenalty=store.FindAppointment(id).NoShowPenalty;
                Check.True(store.TryGetProactiveContactOffer(resident,1602,out var offer));
                Check.Equal(ProactiveContactKind.MissedAppointmentFollowUp,offer.Kind);
                Check.Equal(1601d,offer.ResolvedAt);Check.Equal("missed:meeting-contact:0",offer.ContactId);
                Check.True(store.TryNoticeProactiveContactPlayer(offer,1603,1720));
                Check.True(store.TryInterruptProactiveContactApproach(offer.ContactId,offer.Kind,resident,1605));
                Check.Equal("player-absent",store.FindAppointment(id).Status);
                Check.Equal(originalPenalty,store.FindAppointment(id).NoShowPenalty);
                Check.True(store.TryGetProactiveContactOffer(resident,86401,out var nextDay));
                Check.Equal(ProactiveContactKind.MissedAppointmentFollowUp,nextDay.Kind);
                Check.Equal("missed:meeting-contact:1",nextDay.ContactId);
            })));

            cases.Add(("Familiar proactive greeting carries a real prior exchange and opens once",new Action(()=>{
                var store=new WorldStateStore(71);
                var resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0].Id;
                store.SetConversationBaseline(resident,40,100);
                store.RecordConversationEncounter(resident,100);
                store.RememberConversation(resident,100,"day 1","inn","I like your sister's bakery.","She opened last week.","native");
                store.RecordConversationEncounter(resident,200);
                store.RememberConversation(resident,200,"day 1","inn","I will visit soon.","She would appreciate that.","model");
                Check.True(store.TryGetProactiveContactOffer(resident,86400,out var offer));
                Check.Equal(ProactiveContactKind.FamiliarGreeting,offer.Kind);
                Check.Equal("friendly:"+resident+":1",offer.ContactId);
                Check.True(offer.GroundedPlayerFact.Contains("visit soon"));
                Check.True(offer.GroundedNpcFact.Contains("appreciate"));
                Check.True(store.TryNoticeProactiveContactPlayer(offer,86400,86450));
                Check.True(store.TryClaimProactiveContactConversation(offer.ContactId,offer.Kind,resident,resident,
                    86410,true,true,true,true,out var receipt,out var isNew));
                Check.True(isNew);Check.Equal(offer.GroundedPlayerFact,receipt.GroundedPlayerFact);
                Check.True(!store.TryGetProactiveContactOffer(resident,86420,out _));

                var meeting=new AppointmentRecord{Id="after-friendly",ResidentId=resident,Purpose="scheduled meeting",
                    StartsAt=86420,EndsAt=86480,GraceSeconds=0,PatienceSeconds=600,Importance=1,Status="accepted",
                    Place=new AppointmentPlace{Kind="building",SettlementId=100,BuildingKey=30,Detail="The Blue Room"}};
                Check.True(store.TryReserveAppointment(meeting,86410,out _));
                store.ObserveAppointment(meeting.Id,86420,true,false,false);
                Check.True(store.TryGetProactiveContactOffer(resident,86430,out var meetingOffer));
                Check.Equal(ProactiveContactKind.AppointmentMeeting,meetingOffer.Kind);
                Check.True(store.TryNoticeProactiveContactPlayer(meetingOffer,86430,86500));
                Check.True(store.TryClaimProactiveContactConversation(meetingOffer.ContactId,meetingOffer.Kind,
                    resident,resident,86431,true,true,true,true,out _,out isNew));
                Check.True(isNew); // A scheduled meeting outranks the 10-minute opportunistic cooldown.
                var json=JsonSerializer.Serialize(store.Snapshot(),new JsonSerializerOptions{IncludeFields=true});
                var restored=new WorldStateStore(0);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(json,new JsonSerializerOptions{IncludeFields=true}));
                Check.True(!restored.TryGetProactiveContactOffer(resident,86401,out _));
            })));

            cases.Add(("Player busy pause blocks all proactive starts without cancelling a meeting",new Action(()=>{
                var store=Create(out var resident);store.ObserveAppointment("meeting-contact",1000,true,false,false);
                store.RecordPlayerProactiveContactPause(1000);
                double first=store.Snapshot().PlayerProactiveContactBlockedUntil;
                store.RecordPlayerProactiveContactPause(1000);
                Check.Near(first,store.Snapshot().PlayerProactiveContactBlockedUntil);
                store.RecordPlayerProactiveContactPause(1010);
                double blockedUntil=store.Snapshot().PlayerProactiveContactBlockedUntil;
                Check.True(blockedUntil>=first&&blockedUntil>=22610&&blockedUntil<=44210);
                Check.True(store.IsNpcProactiveContactSuppressed(blockedUntil-0.01));
                Check.True(!store.IsNpcProactiveContactSuppressed(blockedUntil));
                Check.True(!store.TryGetProactiveContactOffer(resident,1011,out _));
                Check.True(!store.TryNoticeAppointmentPlayer("meeting-contact",1011,1100));
                var booking=store.FindAppointment("meeting-contact");
                Check.True(booking.Status=="waiting"&&!booking.OutcomeRecorded);
                var json=JsonSerializer.Serialize(store.Snapshot(),new JsonSerializerOptions{IncludeFields=true});
                var restored=new WorldStateStore(0);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(json,new JsonSerializerOptions{IncludeFields=true}));
                Check.Near(blockedUntil,restored.Snapshot().PlayerProactiveContactBlockedUntil);
            })));
        }
    }
}
