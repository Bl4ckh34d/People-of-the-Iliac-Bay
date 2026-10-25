using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public enum ProactiveContactKind { AppointmentMeeting, MissedAppointmentFollowUp, FamiliarGreeting, CompanionHealthRequest }

    [Serializable]
    public sealed class ProactiveContactRecord
    {
        public string ContactId, ResidentId, AppointmentId, Purpose, GroundedPlayerFact, GroundedNpcFact;
        public ProactiveContactKind Kind;
        public AppointmentPlace Place;
        public double StartsAt, EndsAt, ResolvedAt, NpcArrivedAt, NpcWaitedSeconds;
        public double NoticedAt = -1, ApproachUntil = -1, OpenedAt = -1, GroundedFactAt = -1, InterruptedAt = -1;
        public int PatienceSeconds, ActualDisposition;

        internal static ProactiveContactRecord Copy(ProactiveContactRecord value)
        {
            return new ProactiveContactRecord { ContactId=value.ContactId, ResidentId=value.ResidentId,
                AppointmentId=value.AppointmentId, Purpose=value.Purpose, GroundedPlayerFact=value.GroundedPlayerFact,
                GroundedNpcFact=value.GroundedNpcFact, Kind=value.Kind,
                Place=value.Place==null?null:AppointmentPlace.Copy(value.Place), StartsAt=value.StartsAt,
                EndsAt=value.EndsAt, ResolvedAt=value.ResolvedAt, NpcArrivedAt=value.NpcArrivedAt,
                NpcWaitedSeconds=value.NpcWaitedSeconds, NoticedAt=value.NoticedAt,
                ApproachUntil=value.ApproachUntil, OpenedAt=value.OpenedAt, GroundedFactAt=value.GroundedFactAt,
                PatienceSeconds=value.PatienceSeconds, ActualDisposition=value.ActualDisposition,
                InterruptedAt=value.InterruptedAt };
        }
    }

    public readonly struct ProactiveContactOffer
    {
        public readonly string ContactId, ResidentId, AppointmentId, Purpose;
        public readonly ProactiveContactKind Kind;
        public readonly AppointmentPlace Place;
        public readonly double StartsAt, EndsAt, ResolvedAt, NpcArrivedAt, NpcWaitedSeconds;
        public readonly double NoticedAt, ApproachUntil, GroundedFactAt, PlayerAtVenueAt;
        public readonly string GroundedPlayerFact, GroundedNpcFact;
        public readonly int PatienceSeconds, ActualDisposition;
        internal ProactiveContactOffer(ProactiveContactRecord value, int disposition, double playerAtVenueAt=-1)
        {
            ContactId=value.ContactId;Kind=value.Kind;ResidentId=value.ResidentId;AppointmentId=value.AppointmentId;
            Purpose=value.Purpose;Place=value.Place==null?null:AppointmentPlace.Copy(value.Place);
            StartsAt=value.StartsAt;EndsAt=value.EndsAt;ResolvedAt=value.ResolvedAt;
            NpcArrivedAt=value.NpcArrivedAt;NpcWaitedSeconds=value.NpcWaitedSeconds;
            NoticedAt=value.NoticedAt;ApproachUntil=value.ApproachUntil;GroundedFactAt=value.GroundedFactAt;
            GroundedPlayerFact=value.GroundedPlayerFact;GroundedNpcFact=value.GroundedNpcFact;
            PatienceSeconds=value.PatienceSeconds;ActualDisposition=disposition;PlayerAtVenueAt=playerAtVenueAt;
        }
    }

    public readonly struct ProactiveContactReceipt
    {
        public readonly string ContactId, ResidentId, AppointmentId, Purpose;
        public readonly ProactiveContactKind Kind;
        public readonly AppointmentPlace Place;
        public readonly double StartsAt, EndsAt, ResolvedAt, NpcArrivedAt, NpcWaitedSeconds;
        public readonly double NoticedAt, ApproachUntil, OpenedAt, GroundedFactAt, PlayerArrivedAt, PlayerAtVenueAt;
        public readonly string GroundedPlayerFact, GroundedNpcFact;
        public readonly int PatienceSeconds, ActualDisposition;
        internal ProactiveContactReceipt(ProactiveContactRecord value, int disposition, double playerArrivedAt=-1,
            double playerAtVenueAt=-1)
        {
            ContactId=value.ContactId;Kind=value.Kind;ResidentId=value.ResidentId;AppointmentId=value.AppointmentId;
            Purpose=value.Purpose;Place=value.Place==null?null:AppointmentPlace.Copy(value.Place);
            StartsAt=value.StartsAt;EndsAt=value.EndsAt;ResolvedAt=value.ResolvedAt;
            NpcArrivedAt=value.NpcArrivedAt;NpcWaitedSeconds=value.NpcWaitedSeconds;
            NoticedAt=value.NoticedAt;ApproachUntil=value.ApproachUntil;OpenedAt=value.OpenedAt;
            GroundedFactAt=value.GroundedFactAt;GroundedPlayerFact=value.GroundedPlayerFact;
            GroundedNpcFact=value.GroundedNpcFact;PatienceSeconds=value.PatienceSeconds;
            ActualDisposition=disposition;PlayerArrivedAt=playerArrivedAt;PlayerAtVenueAt=playerAtVenueAt;
        }
    }

    public sealed partial class WorldStateStore
    {
        private const int MaximumRecentProactiveContacts=128;
        private const double ProactiveApproachSeconds=120.0;
        private const double ProactiveGlobalCooldownSeconds=600.0;

        public bool IsNpcProactiveContactSuppressed(double now)
        {return !Finite(now)||now<0||now<state.PlayerProactiveContactBlockedUntil;}

        public void RecordPlayerProactiveContactPause(double requestedAt)
        {
            if(!Finite(requestedAt)||requestedAt<0)throw new ArgumentException("Invalid player contact-pause time.",nameof(requestedAt));
            uint hash=2166136261u;
            string identity=state.WorldSeed.ToString("x8",CultureInfo.InvariantCulture)+"|"+
                requestedAt.ToString("R",CultureInfo.InvariantCulture);
            unchecked
            {
                for(int i=0;i<identity.Length;i++){hash^=identity[i];hash*=16777619u;}
                hash^=0xff;hash*=16777619u;
                if(hash==0)hash=0x9e3779b9u;
                hash^=hash<<13;hash^=hash>>17;hash^=hash<<5;
            }
            const int minimum=6*60*60,maximum=12*60*60;
            int duration=minimum+(int)(hash%(uint)(maximum-minimum+1));
            state.PlayerProactiveContactBlockedUntil=Math.Max(state.PlayerProactiveContactBlockedUntil,requestedAt+duration);
            // Suppression ends any approach arbitration, but does not resolve or
            // alter an appointment: its ordinary evidence observer remains authoritative.
            state.ActiveProactiveContactId=null;
        }

        public bool TryGetProactiveContactOffer(string residentId,double now,out ProactiveContactOffer offer)
        {
            offer=default;
            if(string.IsNullOrWhiteSpace(residentId)||!Finite(now)||now<0)return false;
            if(IsNpcProactiveContactSuppressed(now))return false;
            int disposition=TryGetActualConversationDisposition(residentId,out var actual)?actual:ConversationDisposition(residentId);
            if(disposition<=-100)return false;

            // Find the persisted approach so its owner can resume after higher-priority meetings are checked.
            var active=FindProactiveContact(state.ActiveProactiveContactId);

            foreach(var appointment in state.Appointments)
                if(appointment.ResidentId==residentId && appointment.ConversationOpenedAt<0 &&
                    appointment.NpcArrivedAt>=0 && (appointment.Status=="accepted"||appointment.Status=="waiting"||appointment.Status=="attended") &&
                    (!appointment.OutcomeRecorded||appointment.Status=="attended") && now>=appointment.StartsAt &&
                    now<=AppointmentTiming.ContactDeadline(appointment))
                {
                    var record=BuildAppointmentContact(appointment,ProactiveContactKind.AppointmentMeeting);
                    record.ContactId=MeetingContactId(appointment.Id);
                    var saved=FindProactiveContact(record.ContactId);
                    if(saved!=null && saved.NoticedAt>now)continue;
                    if(saved!=null && saved.OpenedAt>=0)continue;
                    offer=OfferFromRecord(saved??record,disposition,appointment.PlayerAtVenueAt);
                    return true;
                }

            if(!ProactiveGlobalCooldownAllows(now))return false;
            foreach(var appointment in state.Appointments)
                if(appointment.ResidentId==residentId && appointment.Status=="player-absent" &&
                    appointment.OutcomeRecorded && appointment.FollowUpConversationOpenedAt<0 && now>=appointment.ResolvedAt)
                {
                    var record=BuildAppointmentContact(appointment,ProactiveContactKind.MissedAppointmentFollowUp);
                    record.ContactId=MissedContactId(appointment.Id,now);
                    var saved=FindProactiveContact(record.ContactId);
                    if(saved!=null && saved.NoticedAt>now)continue;
                    if(saved!=null && (saved.InterruptedAt>=0||saved.OpenedAt>=0))continue;
                    offer=OfferFromRecord(saved??record,disposition,appointment.PlayerAtVenueAt);
                    return true;
                }

            if(active!=null && active.ResidentId==residentId && now<=active.ApproachUntil &&
                active.NoticedAt<=now && active.OpenedAt<0 && active.InterruptedAt<0)
            {offer=OfferFromRecord(active,disposition);return true;}

            if(!IsEligibleForFriendlyProactiveContact(residentId,now) ||
                !TryGetLatestSharedExchange(residentId,now,out var fact))return false;
            string contactId=FriendlyContactId(residentId,now);
            var savedFriend=FindProactiveContact(contactId);
            if(savedFriend!=null&&(savedFriend.InterruptedAt>=0||savedFriend.GroundedFactAt>now||savedFriend.NoticedAt>now))return false;
            var friend=savedFriend ?? new ProactiveContactRecord {
                ContactId=contactId,ResidentId=residentId,Kind=ProactiveContactKind.FamiliarGreeting,
                Purpose="friendly greeting",Place=null,StartsAt=now,EndsAt=now,ResolvedAt=-1,
                NpcArrivedAt=-1,NpcWaitedSeconds=0,PatienceSeconds=0,NoticedAt=-1,ApproachUntil=-1,
                OpenedAt=-1,GroundedFactAt=fact.At,GroundedPlayerFact=fact.PlayerText,
                GroundedNpcFact=fact.NpcText};
            offer=OfferFromRecord(friend,disposition);
            return true;
        }

        // The native adapter supplies a currently observed poison or disease only
        // for a companion already bound to the player. The ordinary contact ledger
        // owns cooldown, interruption, and one request per resident per game day.
        public bool TryGetCompanionHealthContactOffer(string residentId,string condition,double now,
            out ProactiveContactOffer offer)
        {
            offer=default;
            if(string.IsNullOrWhiteSpace(residentId)||!ValidCompanionCondition(condition)||
                !Finite(now)||now<0||KnownAppointmentResident(state,residentId)==null||
                IsNpcProactiveContactSuppressed(now)||!ProactiveGlobalCooldownAllows(now))return false;
            int disposition=TryGetActualConversationDisposition(residentId,out var actual)?actual:ConversationDisposition(residentId);
            if(disposition<=-100)return false;
            if(TryGetProactiveContactOffer(residentId,now,out var prior) &&
                (prior.Kind==ProactiveContactKind.AppointmentMeeting||
                 prior.Kind==ProactiveContactKind.MissedAppointmentFollowUp))return false;
            string id=CompanionHealthContactId(residentId,now);
            var saved=FindProactiveContact(id);
            if(saved!=null)
            {
                if(saved.Kind!=ProactiveContactKind.CompanionHealthRequest||saved.Purpose!=condition||
                    saved.InterruptedAt>=0||saved.OpenedAt>=0||saved.NoticedAt>now)return false;
                offer=OfferFromRecord(saved,disposition);
                return true;
            }
            var active=FindProactiveContact(state.ActiveProactiveContactId);
            if(active!=null && active.ContactId!=id && now<=active.ApproachUntil)return false;
            var record=new ProactiveContactRecord {ContactId=id,ResidentId=residentId,
                Kind=ProactiveContactKind.CompanionHealthRequest,Purpose=condition,StartsAt=now,EndsAt=now,
                ResolvedAt=-1,NpcArrivedAt=-1,NoticedAt=-1,ApproachUntil=-1,OpenedAt=-1,GroundedFactAt=now};
            offer=OfferFromRecord(record,disposition);
            return true;
        }

        public bool TryNoticeProactiveContactPlayer(ProactiveContactOffer offer,double now,double latestSafeApproachAt)
        {
            if(string.IsNullOrWhiteSpace(offer.ContactId)||string.IsNullOrWhiteSpace(offer.ResidentId)||
                !Finite(now)||now<0||!Finite(latestSafeApproachAt)||latestSafeApproachAt<0)
                return false;
            if(IsNpcProactiveContactSuppressed(now))return false;
            int noticeDisposition=TryGetActualConversationDisposition(offer.ResidentId,out var observed)?observed:
                ConversationDisposition(offer.ResidentId);
            if(noticeDisposition<=-100)return false;
            var saved=FindProactiveContact(offer.ContactId);
            if(saved!=null&&saved.NoticedAt>now)return false;
            if(saved!=null && saved.InterruptedAt>=0)return false;
            if(saved!=null && saved.NoticedAt>=0)return saved.OpenedAt<0 && saved.ApproachUntil>now;
            if(!ProactiveGlobalCooldownAllows(now) && offer.Kind!=ProactiveContactKind.AppointmentMeeting)return false;
            var active=FindProactiveContact(state.ActiveProactiveContactId);
            if(active!=null && active.ContactId!=offer.ContactId && now<=active.ApproachUntil)
            {
                if(offer.Kind!=ProactiveContactKind.AppointmentMeeting || active.Kind==ProactiveContactKind.AppointmentMeeting)
                    return false;
                active.InterruptedAt=now;
                state.ActiveProactiveContactId=null;
            }

            ProactiveContactRecord record=saved??RecordFromOffer(offer);
            if(offer.Kind==ProactiveContactKind.MissedAppointmentFollowUp)
            {
                var appointment=state.Appointments.Find(item=>item.Id==offer.AppointmentId && item.ResidentId==offer.ResidentId);
                if(appointment==null || appointment.Status!="player-absent" || appointment.FollowUpConversationOpenedAt>=0 ||
                    record.ContactId!=MissedContactId(offer.AppointmentId,now))return false;
            }
            if(offer.Kind==ProactiveContactKind.FamiliarGreeting &&
                !IsEligibleForFriendlyProactiveContact(offer.ResidentId,now))return false;
            if(offer.Kind==ProactiveContactKind.CompanionHealthRequest &&
                (!ValidCompanionCondition(offer.Purpose)||offer.ContactId!=CompanionHealthContactId(offer.ResidentId,now)))return false;
            double safe=Math.Min(latestSafeApproachAt,now+ProactiveApproachSeconds);
            foreach(var appointment in state.Appointments)
                if(appointment.ResidentId==offer.ResidentId && appointment.Id!=offer.AppointmentId &&
                    (appointment.Status=="accepted"||appointment.Status=="waiting") && appointment.StartsAt>now && appointment.StartsAt<safe)
                    safe=appointment.StartsAt;
            foreach(var trip in state.ResidentTrips)
                if(trip.ResidentId==offer.ResidentId && trip.HomeLeavesAt>now && trip.HomeLeavesAt<safe)safe=trip.HomeLeavesAt;
            record.NoticedAt=now;record.ApproachUntil=Math.Max(now,safe);record.ActualDisposition=noticeDisposition;
            if(offer.Kind==ProactiveContactKind.AppointmentMeeting)
            {
                if(!TryNoticeAppointmentPlayer(offer.AppointmentId,now,record.ApproachUntil))return false;
                var appointment=state.Appointments.Find(item=>item.Id==offer.AppointmentId && item.ResidentId==offer.ResidentId);
                if(appointment==null)return false;
                record=BuildAppointmentContact(appointment,offer.Kind,offer.ContactId);
                record.NoticedAt=appointment.PlayerNoticedAt;record.ApproachUntil=appointment.ApproachUntil;
            }
            else if(offer.Kind==ProactiveContactKind.MissedAppointmentFollowUp)
            {
                var appointment=state.Appointments.Find(item=>item.Id==offer.AppointmentId && item.ResidentId==offer.ResidentId);
                if(appointment==null)return false;
                appointment.FollowUpNoticedAt=now;appointment.FollowUpApproachUntil=record.ApproachUntil;
                BumpAppointmentRevision();
            }
            if(saved==null)AppendProactiveContact(record);
            else
            {
                saved.NoticedAt=record.NoticedAt;saved.ApproachUntil=record.ApproachUntil;
                saved.Place=record.Place==null?null:AppointmentPlace.Copy(record.Place);
                saved.Purpose=record.Purpose;saved.StartsAt=record.StartsAt;saved.EndsAt=record.EndsAt;
                saved.ResolvedAt=record.ResolvedAt;saved.NpcArrivedAt=record.NpcArrivedAt;
                saved.NpcWaitedSeconds=record.NpcWaitedSeconds;saved.PatienceSeconds=record.PatienceSeconds;
            }
            state.ActiveProactiveContactId=record.ContactId;
            TrimProactiveContacts();
            return record.ApproachUntil>now;
        }

        // Runtime calls this only after an observed approach route or LOS has failed.
        // Scene teardown and ordinary player departure must not consume an attempt.
        public bool TryInterruptProactiveContactApproach(string contactId,ProactiveContactKind kind,
            string expectedResidentId,double now)
        {
            if(string.IsNullOrWhiteSpace(contactId)||string.IsNullOrWhiteSpace(expectedResidentId)||!Finite(now)||now<0||
                !string.Equals(state.ActiveProactiveContactId,contactId,StringComparison.Ordinal))return false;
            var record=FindProactiveContact(contactId);
            if(record==null||record.Kind!=kind||record.ResidentId!=expectedResidentId||record.NoticedAt<0||
                record.OpenedAt>=0||record.InterruptedAt>=0||now<record.NoticedAt)return false;
            if(kind==ProactiveContactKind.AppointmentMeeting)
            {
                var appointment=state.Appointments.Find(item=>item.Id==record.AppointmentId&&item.ResidentId==expectedResidentId);
                if(appointment==null||appointment.OutcomeRecorded)return false;
                double baseDeadline=AppointmentTiming.ArrivalDeadline(appointment);
                if(now>baseDeadline)
                {
                    appointment.ApproachUntil=Math.Min(appointment.ApproachUntil,appointment.PlayerNoticedAt);
                    BumpAppointmentRevision();
                    bool npcSeenRecently=appointment.NpcLastSeenAt>=0&&now-appointment.NpcLastSeenAt<=15;
                    ObserveAppointment(appointment.Id,now,npcSeenRecently,false,false,false);
                }
                else
                    ResolveAppointment(appointment,now,"interrupted",0,
                        "The player and I could not complete our approach to the " + appointment.Purpose + " meeting.");
            }
            record.InterruptedAt=now;
            state.ActiveProactiveContactId=null;
            BumpAppointmentRevision();
            return true;
        }

        public bool TryClaimProactiveContactConversation(string contactId,ProactiveContactKind kind,
            string expectedResidentId,string actualSpeakerId,double now,bool windowOpen,bool allowed,
            bool npcNear,bool playerNear,out ProactiveContactReceipt receipt,out bool isNew)
        {
            receipt=default;isNew=false;
            if(string.IsNullOrWhiteSpace(contactId)||string.IsNullOrWhiteSpace(expectedResidentId)||
                !string.Equals(expectedResidentId,actualSpeakerId,StringComparison.Ordinal)||!Finite(now)||now<0)return false;
            var record=FindProactiveContact(contactId);
            if(record==null||record.Kind!=kind||record.ResidentId!=expectedResidentId)return false;
            if(record.OpenedAt>=0)
            {if(now<record.OpenedAt)return false;receipt=ReceiptFromRecord(record);return true;}
            if(IsNpcProactiveContactSuppressed(now))return false;
            if(!string.Equals(state.ActiveProactiveContactId,contactId,StringComparison.Ordinal))return false;
            if(!windowOpen||!allowed||!npcNear||!playerNear||record.NoticedAt<0||now>record.ApproachUntil||
                (kind!=ProactiveContactKind.AppointmentMeeting&&!ProactiveGlobalCooldownAllows(now)))return false;
            int disposition=TryGetActualConversationDisposition(expectedResidentId,out var actual)?actual:ConversationDisposition(expectedResidentId);
            if(disposition<=-100)return false;
            double playerArrived=-1,playerAtVenue=now;
            if(kind==ProactiveContactKind.AppointmentMeeting)
            {
                if(!TryClaimAppointmentConversation(record.AppointmentId,expectedResidentId,actualSpeakerId,now,
                    windowOpen,allowed,npcNear,playerNear,out var meeting,out _))return false;
                playerArrived=meeting.PlayerArrivedAt;playerAtVenue=now;
                var appointment=state.Appointments.Find(item=>item.Id==record.AppointmentId);
                if(appointment!=null)RefreshContactFromAppointment(record,appointment);
            }
            else if(kind==ProactiveContactKind.MissedAppointmentFollowUp)
            {
                var appointment=state.Appointments.Find(item=>item.Id==record.AppointmentId && item.ResidentId==expectedResidentId);
                if(appointment==null||appointment.Status!="player-absent"||appointment.FollowUpConversationOpenedAt>=0)return false;
                appointment.FollowUpConversationOpenedAt=now;appointment.FollowUpNoticedAt=record.NoticedAt;
                appointment.FollowUpApproachUntil=record.ApproachUntil;BumpAppointmentRevision();
            }
            else if(kind==ProactiveContactKind.FamiliarGreeting)
            {
                if(record.GroundedFactAt<0||string.IsNullOrWhiteSpace(record.GroundedPlayerFact)||
                    string.IsNullOrWhiteSpace(record.GroundedNpcFact)||record.ActualDisposition<30||disposition<30)return false;
                RecordFriendlyProactiveContact(expectedResidentId,now);
            }
            else if(kind==ProactiveContactKind.CompanionHealthRequest)
            {
                if(!ValidCompanionCondition(record.Purpose)||
                    record.ContactId!=CompanionHealthContactId(expectedResidentId,record.StartsAt))return false;
            }
            else return false;

            record.OpenedAt=now;state.LastProactiveContactAt=now;state.LastProactiveContactId=record.ContactId;
            state.ActiveProactiveContactId=null;BumpAppointmentRevision();
            receipt=new ProactiveContactReceipt(record,disposition,playerArrived,playerAtVenue);isNew=true;
            return true;
        }

        private bool ProactiveGlobalCooldownAllows(double now)
        {return string.IsNullOrEmpty(state.LastProactiveContactId)||now-state.LastProactiveContactAt>=ProactiveGlobalCooldownSeconds;}

        private ProactiveContactRecord FindProactiveContact(string id)
        {return string.IsNullOrEmpty(id)?null:state.RecentProactiveContacts.Find(item=>item.ContactId==id);}

        private void AppendProactiveContact(ProactiveContactRecord record)
        {state.RecentProactiveContacts.Add(ProactiveContactRecord.Copy(record));}

        private void TrimProactiveContacts()
        {
            while(state.RecentProactiveContacts.Count>MaximumRecentProactiveContacts)
            {
                int index=state.RecentProactiveContacts.FindIndex(item=>item.ContactId!=state.ActiveProactiveContactId&&
                    item.ContactId!=state.LastProactiveContactId);
                if(index<0)break;
                state.RecentProactiveContacts.RemoveAt(index);
            }
        }

        private static ProactiveContactRecord BuildAppointmentContact(AppointmentRecord appointment,ProactiveContactKind kind)
        {
            return new ProactiveContactRecord {ResidentId=appointment.ResidentId,AppointmentId=appointment.Id,Kind=kind,
                Purpose=appointment.Purpose,Place=AppointmentPlace.Copy(appointment.Place),StartsAt=appointment.StartsAt,
                EndsAt=appointment.EndsAt,ResolvedAt=appointment.ResolvedAt,NpcArrivedAt=appointment.NpcArrivedAt,
                NpcWaitedSeconds=appointment.NpcWaitedSeconds,PatienceSeconds=appointment.PatienceSeconds,
                NoticedAt=kind==ProactiveContactKind.AppointmentMeeting?appointment.PlayerNoticedAt:appointment.FollowUpNoticedAt,
                ApproachUntil=kind==ProactiveContactKind.AppointmentMeeting?appointment.ApproachUntil:appointment.FollowUpApproachUntil,
                OpenedAt=kind==ProactiveContactKind.AppointmentMeeting?appointment.ConversationOpenedAt:appointment.FollowUpConversationOpenedAt};
        }

        private ProactiveContactReceipt ReceiptFromRecord(ProactiveContactRecord record)
        {
            var appointment=string.IsNullOrEmpty(record.AppointmentId)?null:state.Appointments.Find(item=>item.Id==record.AppointmentId);
            return new ProactiveContactReceipt(record,
                TryGetActualConversationDisposition(record.ResidentId,out var actual)?actual:ConversationDisposition(record.ResidentId),
                appointment==null?-1:appointment.PlayerArrivedAt,appointment==null?-1:appointment.PlayerAtVenueAt);
        }

        private static ProactiveContactRecord RecordFromOffer(ProactiveContactOffer offer)
        {
            return new ProactiveContactRecord {ContactId=offer.ContactId,Kind=offer.Kind,ResidentId=offer.ResidentId,
                AppointmentId=offer.AppointmentId,Purpose=offer.Purpose,Place=offer.Place==null?null:AppointmentPlace.Copy(offer.Place),
                StartsAt=offer.StartsAt,EndsAt=offer.EndsAt,ResolvedAt=offer.ResolvedAt,NpcArrivedAt=offer.NpcArrivedAt,
                NpcWaitedSeconds=offer.NpcWaitedSeconds,PatienceSeconds=offer.PatienceSeconds,
                GroundedFactAt=offer.GroundedFactAt,GroundedPlayerFact=offer.GroundedPlayerFact,GroundedNpcFact=offer.GroundedNpcFact};
        }

        private static ProactiveContactRecord BuildAppointmentContact(AppointmentRecord appointment,ProactiveContactKind kind,
            string contactId)
        {var record=BuildAppointmentContact(appointment,kind);record.ContactId=contactId;return record;}

        private void RefreshContactFromAppointment(ProactiveContactRecord record,AppointmentRecord appointment)
        {
            var refreshed=BuildAppointmentContact(appointment,record.Kind,record.ContactId);
            record.Place=refreshed.Place;record.Purpose=refreshed.Purpose;record.StartsAt=refreshed.StartsAt;
            record.EndsAt=refreshed.EndsAt;record.ResolvedAt=refreshed.ResolvedAt;
            record.NpcArrivedAt=refreshed.NpcArrivedAt;record.NpcWaitedSeconds=refreshed.NpcWaitedSeconds;
            record.PatienceSeconds=refreshed.PatienceSeconds;
        }

        private static string MeetingContactId(string appointmentId){return "meeting:"+appointmentId;}
        private static string MissedContactId(string appointmentId,double now)
        {return "missed:"+appointmentId+":"+Math.Floor(now/86400).ToString("0",CultureInfo.InvariantCulture);}
        private static string FriendlyContactId(string residentId,double now)
        {return "friendly:"+residentId+":"+Math.Floor(now/86400).ToString("0",CultureInfo.InvariantCulture);}
        private static string CompanionHealthContactId(string residentId,double now)
        {return "companion-health:"+residentId+":"+Math.Floor(now/86400).ToString("0",CultureInfo.InvariantCulture);}
        private static bool ValidCompanionCondition(string condition)
        {return condition=="poison"||condition=="disease";}

        private static List<ProactiveContactRecord> CopyProactiveContacts(WorldState source,WorldState validatedWorld)
        {
            var result=new List<ProactiveContactRecord>();
            if(source.RecentProactiveContacts==null)return result;
            Require(source.RecentProactiveContacts.Count<=MaximumRecentProactiveContacts,
                "Oversized proactive-contact receipt ledger.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in source.RecentProactiveContacts)
            {
                Require(item!=null&&!string.IsNullOrWhiteSpace(item.ContactId)&&item.ContactId.Length<=400&&
                    !string.IsNullOrWhiteSpace(item.ResidentId)&&item.ResidentId.Length<=240&&ids.Add(item.ContactId)&&
                    (item.Kind==ProactiveContactKind.AppointmentMeeting||
                     item.Kind==ProactiveContactKind.MissedAppointmentFollowUp||
                     item.Kind==ProactiveContactKind.FamiliarGreeting||
                     item.Kind==ProactiveContactKind.CompanionHealthRequest)&&
                    (item.AppointmentId==null||item.AppointmentId.Length<=120)&&
                    (item.Purpose==null||item.Purpose.Length<=160)&&
                    (item.GroundedPlayerFact==null||item.GroundedPlayerFact.Length<=600)&&
                    (item.GroundedNpcFact==null||item.GroundedNpcFact.Length<=1600)&&
                    Finite(item.StartsAt)&&item.StartsAt>=0&&Finite(item.EndsAt)&&item.EndsAt>=item.StartsAt&&
                    (item.ResolvedAt==-1||Finite(item.ResolvedAt)&&item.ResolvedAt>=0)&&
                    (item.NpcArrivedAt==-1||Finite(item.NpcArrivedAt)&&item.NpcArrivedAt>=item.StartsAt)&&
                    Finite(item.NpcWaitedSeconds)&&item.NpcWaitedSeconds>=0&&
                    (item.NoticedAt==-1||Finite(item.NoticedAt)&&item.NoticedAt>=0)&&
                    (item.ApproachUntil==-1||Finite(item.ApproachUntil)&&item.NoticedAt>=0&&
                        item.ApproachUntil>=item.NoticedAt&&item.ApproachUntil<=item.NoticedAt+ProactiveApproachSeconds)&&
                    (item.OpenedAt==-1||Finite(item.OpenedAt)&&item.OpenedAt>=0)&&
                    (item.InterruptedAt==-1||Finite(item.InterruptedAt)&&item.InterruptedAt>=0)&&
                    (item.GroundedFactAt==-1||Finite(item.GroundedFactAt)&&item.GroundedFactAt>=0)&&
                    item.PatienceSeconds>=0&&item.PatienceSeconds<=AppointmentTiming.MaximumPatienceSeconds&&
                    item.ActualDisposition>=-100&&item.ActualDisposition<=100,
                    "Invalid proactive-contact receipt.");
                if(item.Place!=null)ValidateAppointmentPlace(item.Place);
                if(item.Kind==ProactiveContactKind.AppointmentMeeting||item.Kind==ProactiveContactKind.MissedAppointmentFollowUp)
                {
                    Require(!string.IsNullOrWhiteSpace(item.AppointmentId)&&
                        validatedWorld.Appointments.Exists(a=>a.Id==item.AppointmentId&&a.ResidentId==item.ResidentId),
                        "Proactive appointment contact lacks its booking.");
                }
                else Require(string.IsNullOrEmpty(item.AppointmentId),"Friendly contact cannot name an appointment.");
                if(item.Kind==ProactiveContactKind.CompanionHealthRequest)
                    Require(ValidCompanionCondition(item.Purpose)&&
                        item.ContactId==CompanionHealthContactId(item.ResidentId,item.StartsAt),
                        "Companion health contact lacks its observed condition.");
                Require(KnownAppointmentResident(validatedWorld,item.ResidentId)!=null,
                    "Proactive contact resident is unknown.");
                result.Add(ProactiveContactRecord.Copy(item));
            }
            return result;
        }

        private static void ValidateProactiveContactPointers(WorldState world)
        {
            Require(Finite(world.LastProactiveContactAt),"Invalid last proactive-contact time.");
            if(string.IsNullOrEmpty(world.LastProactiveContactId))
            {
                if(world.LastProactiveContactAt==0)world.LastProactiveContactAt=-1; // legacy field default
                Require(world.LastProactiveContactAt==-1,"Proactive-contact time lacks its receipt ID.");
            }
            else
            {
                var last=world.RecentProactiveContacts.Find(item=>item.ContactId==world.LastProactiveContactId);
                Require(world.LastProactiveContactAt>=0&&last!=null&&last.OpenedAt==world.LastProactiveContactAt,
                    "Last proactive-contact pointer lacks its opened receipt.");
            }
            if(!string.IsNullOrEmpty(world.ActiveProactiveContactId))
            {
                var active=world.RecentProactiveContacts.Find(item=>item.ContactId==world.ActiveProactiveContactId);
                Require(active!=null&&active.NoticedAt>=0&&active.OpenedAt<0&&active.InterruptedAt<0&&
                    active.ApproachUntil>=active.NoticedAt,
                    "Active proactive-contact pointer lacks an eligible notice receipt.");
            }
        }

        private static ProactiveContactOffer OfferFromRecord(ProactiveContactRecord value,int disposition,
            double playerAtVenue=-1)
        {return new ProactiveContactOffer(value,disposition,playerAtVenue);}
    }
}
