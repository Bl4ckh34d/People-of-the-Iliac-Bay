using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Shared integer arithmetic for one night-rest treatment quote.</summary>
    public static class NpcHealthRestQuoteMath
    {
        public const int GameHoursPerNight=8;
        public const int MaximumNights=350;
        public const int SecondsPerGameHour=3600;

        public static int RequiredGameHours(int missingHealth,int recoveryPerGameHour)
        {
            if(missingHealth<1 || recoveryPerGameHour<1)throw new ArgumentOutOfRangeException();
            return (missingHealth-1)/recoveryPerGameHour+1;
        }

        public static int NightsFor(int missingHealth,int recoveryPerGameHour)
        {
            int hours=RequiredGameHours(missingHealth,recoveryPerGameHour);
            return (hours-1)/GameHoursPerNight+1;
        }

        public static int NightsForDurationSeconds(double durationSeconds)
        {
            if(double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds) || durationSeconds<=0)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            return (int)Math.Ceiling(durationSeconds/(GameHoursPerNight*(double)SecondsPerGameHour));
        }

        public static double NextRestDurationSeconds(int missingHealth,int recoveryPerGameHour)
        {
            int hours=RequiredGameHours(missingHealth,recoveryPerGameHour);
            return Math.Min(hours,GameHoursPerNight)*(double)SecondsPerGameHour;
        }

        public static int ExpectedGain(int missingHealth,int recoveryPerGameHour,double durationSeconds)
        {
            if(missingHealth<1 || recoveryPerGameHour<1 || double.IsNaN(durationSeconds) ||
                double.IsInfinity(durationSeconds) || durationSeconds<=0)throw new ArgumentOutOfRangeException();
            long hours=(long)Math.Floor(durationSeconds/SecondsPerGameHour);
            if(hours<1)throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            return (int)Math.Min(missingHealth,(long)recoveryPerGameHour*hours);
        }
    }

    /// <summary>Exact, quoted booking request made only after a health task is accepted and bound.</summary>
    public sealed class NpcHealthLodgingReservationRequest
    {
        public string ResidentId,CareEpisodeId,TaskId,RoomId,BedId;
        public long ExpectedCareRevision,ExpectedInventoryRevision;
        public int SettlementId,BuildingKey,Nights,Price;
        public double StartsAt;
    }

    /// <summary>Detached evidence for a paid room reserved for one care episode.</summary>
    public sealed class NpcHealthLodgingReservation
    {
        public string ResidentId,CareEpisodeId,TaskId,RoomId,BedId,PaymentTransactionId;
        public int SettlementId,BuildingKey,Nights,Price;
        public double StartsAt,EndsAt;

        internal static NpcHealthLodgingReservation From(int town,int building,LodgingBooking booking)
        {
            if(booking==null)return null;
            return new NpcHealthLodgingReservation {ResidentId=booking.GuestId,CareEpisodeId=booking.HealthCareId,
                TaskId=booking.HealthCareTaskId,RoomId=booking.RoomId,BedId=booking.BedId,
                PaymentTransactionId=booking.PaymentTransactionId,SettlementId=town,BuildingKey=building,
                Nights=booking.PaidNights,Price=booking.PaidPrice,StartsAt=booking.StartsAt,EndsAt=booking.EndsAt};
        }
    }

    /// <summary>Bounded retirement pass counts examined rows, removals, and live authority pins.</summary>
    public sealed class NpcHealthLodgingRetirementResult
    {
        public int Scanned,Retired,PinnedByAuthority,PinnedByTask,PinnedByCare,PinnedByAction,PinnedByTravel;
    }

    public sealed partial class WorldStateStore
    {
        private const double HealthLodgingSecondsPerNight=86400d;
        private const int MaximumHealthLodgingNights=350;
        private WorldState healthLodgingRetirementCursorState;
        private int healthLodgingRetirementInnCursor,healthLodgingRetirementBookingCursor;

        /// <summary>
        /// Retires only expired, restore-valid healthcare bookings whose paid task is
        /// terminal and unreferenced. The supplied time must be the authoritative
        /// game clock. The runtime invokes a small batch while gameplay is ready.
        /// Call before any terminal-task pruning and preserve remaining booking pins.
        /// </summary>
        public NpcHealthLodgingRetirementResult RetireExpiredNpcHealthRestLodging(double gameSeconds,int maxRows)
        {
            if(!DefenseTime(gameSeconds))throw new ArgumentOutOfRangeException(nameof(gameSeconds));
            if(maxRows<1 || maxRows>128)throw new ArgumentOutOfRangeException(nameof(maxRows));
            var result=new NpcHealthLodgingRetirementResult();
            if(!ReferenceEquals(healthLodgingRetirementCursorState,state))
            {
                healthLodgingRetirementCursorState=state;
                healthLodgingRetirementInnCursor=0;
                healthLodgingRetirementBookingCursor=0;
            }
            while(result.Scanned<maxRows && state.Lodgings.Count>0)
            {
                if(healthLodgingRetirementInnCursor>=state.Lodgings.Count)healthLodgingRetirementInnCursor=0;
                TavernLodging inn=state.Lodgings[healthLodgingRetirementInnCursor];
                if(healthLodgingRetirementBookingCursor>=inn.Bookings.Count)
                {
                    if(inn.Bookings.Count==0)result.Scanned++;
                    healthLodgingRetirementInnCursor++;
                    healthLodgingRetirementBookingCursor=0;
                    continue;
                }
                LodgingBooking booking=inn.Bookings[healthLodgingRetirementBookingCursor];
                result.Scanned++;
                if(booking.Purpose==LodgingBookingPurpose.NpcHealthRest && booking.EndsAt<=gameSeconds &&
                    RetirableNpcHealthRestLodging(inn,booking,result))
                {
                    inn.Bookings.RemoveAt(healthLodgingRetirementBookingCursor);
                    result.Retired++;
                }
                else healthLodgingRetirementBookingCursor++;
            }
            return result;
        }

        private bool RetirableNpcHealthRestLodging(TavernLodging inn,LodgingBooking booking,
            NpcHealthLodgingRetirementResult result)
        {
            // The task is the non-replayable payment authority. A missing task is
            // never a reason to erase a booking; retirement must precede task prune.
            if(!ValidHealthRestLodgingAuthority(state,inn,booking))
            {result.PinnedByAuthority++;return false;}
            NpcTaskPlan task=state.NpcTasks.Find(value=>value!=null && value.Id==booking.HealthCareTaskId);
            if(task==null || !NpcTaskContract.IsTerminal(task))
            {result.PinnedByTask++;return false;}
            var episodeTasks=new HashSet<string>(StringComparer.Ordinal);
            var episodeActions=new HashSet<string>(StringComparer.Ordinal);
            foreach(var current in state.NpcTasks)
                if(HealthRestTaskUsesBooking(current,inn,booking))
                {
                    episodeTasks.Add(current.Id);
                    // A validated health-rest task has exactly one step.
                    episodeActions.Add(current.Id+":step:0");
                }
            if(state.NpcHealthCares.Exists(value=>value!=null &&
                value.PersonId==booking.GuestId && value.Id==booking.HealthCareId &&
                value.TaskId!=null && episodeTasks.Contains(value.TaskId)))
            {result.PinnedByCare++;return false;}
            if(state.NpcActions.Exists(action=>action!=null && action.ResidentId==booking.GuestId &&
                episodeActions.Contains(action.Id) &&
                (!NpcActionExecutor.IsTerminal(action) || action.ReturningToRoutine)))
            {result.PinnedByAction++;return false;}
            if(state.TravelerVisits.Exists(value=>value!=null && value.BookingId==booking.Id) ||
                state.ResidentTrips.Exists(value=>value!=null && value.InnStays!=null &&
                    value.InnStays.Exists(stay=>stay!=null && stay.BookingId==booking.Id)))
            {result.PinnedByTravel++;return false;}
            return true;
        }

        private static bool HealthRestTaskUsesBooking(NpcTaskPlan task,TavernLodging inn,LodgingBooking booking)
        {
            return task!=null && task.ResidentId==booking.GuestId &&
                task.Id.StartsWith("npc-health-care:"+booking.HealthCareId+":attempt:",StringComparison.Ordinal) &&
                TaskTargetsHealthRestBed(task,inn.SettlementId,inn.BuildingKey,booking.BedId,false);
        }

        /// <summary>
        /// Atomically saves a rented Inn room and debits the resident's wallet.
        /// The request must match the currently accepted, care-bound RestToHeal task.
        /// </summary>
        public bool TryReserveNpcHealthRestLodging(NpcHealthLodgingReservationRequest request,
            out NpcHealthLodgingReservation reservation)
        {
            reservation=null;
            if(request==null || !ValidId(request.ResidentId) || !NpcHealthCare.ValidEpisodeId(request.CareEpisodeId) ||
                !ValidId(request.TaskId) || !ValidId(request.RoomId) || !ValidId(request.BedId) ||
                request.SettlementId<0 || request.BuildingKey<0 || request.ExpectedCareRevision<1 ||
                request.ExpectedInventoryRevision<0 || request.Nights<1 || request.Nights>MaximumHealthLodgingNights ||
                request.Price<0 || request.Price>NpcInventoryLimits.MaximumWalletGold || !DefenseTime(request.StartsAt))return false;

            NpcHealthCareRecord care=ReadNpcHealthCare(request.ResidentId);
            NpcTaskPlan task=ReadNpcTask(request.TaskId);
            Resident resident=ResolveResident(request.ResidentId);
            if(care==null || care.Id!=request.CareEpisodeId || care.Revision!=request.ExpectedCareRevision ||
                care.Phase!=NpcHealthCarePhase.Acting || care.TaskId!=request.TaskId || resident==null ||
                !resident.IsAlive || resident.IsQuestProtected || resident.AgeGroup!=ResidentAgeGroup.Adult ||
                task==null || task.Phase!=NpcTaskPhase.Ready || task.CurrentStepIndex!=0 ||
                task.ResidentId!=request.ResidentId || ActiveNpcTask(request.ResidentId)==null ||
                ActiveNpcTask(request.ResidentId).Id!=request.TaskId ||
                !TaskTargetsHealthRestBed(task,request.SettlementId,request.BuildingKey,request.BedId,true))return false;

            double endsAt=request.StartsAt+request.Nights*HealthLodgingSecondsPerNight;
            if(!DefenseTime(endsAt) || endsAt<=request.StartsAt || task.Steps[0].DurationSeconds<=0 ||
                request.StartsAt<task.AcceptedAt || request.StartsAt>=task.DeadlineAt ||
                endsAt<request.StartsAt+task.Steps[0].DurationSeconds)return false;

            TavernLodging tavern=state.Lodgings.Find(value=>value.SettlementId==request.SettlementId && value.BuildingKey==request.BuildingKey);
            if(tavern==null)return false;
            LodgingRoom room=tavern.Rooms.Find(value=>value.Id==request.RoomId && !value.NonRentable && value.Beds.Contains(request.BedId));
            if(room==null || room.Beds.Count<1)return false;

            string bookingId=HealthLodgingBookingId(request.TaskId);
            string paymentId=HealthLodgingPaymentId(request.TaskId);
            foreach(TavernLodging other in state.Lodgings)
            {
                LodgingBooking prior=other.Bookings.Find(value=>value.Id==bookingId);
                if(prior==null)continue;
                if(other!=tavern || !SameHealthBooking(prior,request,endsAt,bookingId,paymentId))return false;
                reservation=NpcHealthLodgingReservation.From(other.SettlementId,other.BuildingKey,prior);
                return true;
            }

            // An active booking for this episode is reused; it keeps the NPC's
            // bed access through later rest attempts without charging twice.
            NpcHealthLodgingReservation existing=NpcHealthRestLodging(request.ResidentId,request.CareEpisodeId,request.StartsAt);
            if(existing!=null)
            {
                if(existing.SettlementId!=request.SettlementId || existing.BuildingKey!=request.BuildingKey ||
                    existing.RoomId!=request.RoomId || existing.BedId!=request.BedId || existing.EndsAt<endsAt)return false;
                reservation=existing;
                return true;
            }

            if(OccupiedRoom(tavern,room,endsAt) || tavern.Bookings.Exists(value=>value.RoomId==room.Id && Overlap(request.StartsAt,endsAt,value)))return false;
            foreach(TavernLodging other in state.Lodgings)
                foreach(LodgingBooking prior in other.Bookings)
                    if(prior.GuestId==request.ResidentId && (prior.Player || Overlap(request.StartsAt,endsAt,prior)))return false;

            NpcInventoryState inventory=IndexedNpcInventory(request.ResidentId);
            int inventoryIndex=inventory==null?-1:state.NpcInventories.IndexOf(inventory);
            if(inventoryIndex<0 || inventory.Revision!=request.ExpectedInventoryRevision ||
                inventory.Revision>=NpcInventoryLimits.MaximumRevision || inventory.WalletGold<request.Price)return false;
            NpcInventoryState nextInventory=CopyNpcInventory(inventory);
            nextInventory.WalletGold-=request.Price;
            nextInventory.Revision++;
            nextInventory.LastTransactionId=paymentId;
            var inventoryUids=new HashSet<ulong>();
            foreach(NpcInventoryState other in state.NpcInventories)
                if(other!=inventory)foreach(NpcInventoryItem item in other.Items)inventoryUids.Add(item.Uid);
            try {ValidateNpcInventory(nextInventory,inventoryUids);}
            catch(ArgumentException) {return false;}

            var candidate=new TavernLodging {SettlementId=tavern.SettlementId,BuildingKey=tavern.BuildingKey,
                Rooms=tavern.Rooms,Occupants=tavern.Occupants,Bookings=new List<LodgingBooking>(tavern.Bookings)};
            candidate.Bookings.Add(new LodgingBooking {Id=bookingId,GuestId=request.ResidentId,
                HomeSettlement=request.SettlementId,Player=false,PartySize=1,RoomId=request.RoomId,
                StartsAt=request.StartsAt,EndsAt=endsAt,Purpose=LodgingBookingPurpose.NpcHealthRest,
                HealthCareId=request.CareEpisodeId,HealthCareTaskId=request.TaskId,BedId=request.BedId,
                PaymentTransactionId=paymentId,PaidPrice=request.Price,PaidNights=request.Nights});
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(TavernLodging other in state.Lodgings)
                if(other!=tavern)foreach(LodgingBooking booking in other.Bookings)ids.Add(booking.Id);
            TavernLodging nextTavern;
            try {nextTavern=CopyLodging(candidate,state,ids,WorldState.CurrentVersion,state);}
            catch(ArgumentException) {return false;}
            int lodgingIndex=state.Lodgings.IndexOf(tavern);
            if(lodgingIndex<0 || !ReferenceEquals(state.Lodgings[lodgingIndex],tavern) ||
                !ReferenceEquals(state.NpcInventories[inventoryIndex],inventory))return false;

            // Everything is copied and validated before either ledger publishes.
            state.Lodgings[lodgingIndex]=nextTavern;
            state.NpcInventories[inventoryIndex]=nextInventory;
            npcInventoryIndex[request.ResidentId]=nextInventory;
            reservation=NpcHealthLodgingReservation.From(request.SettlementId,request.BuildingKey,
                nextTavern.Bookings.Find(value=>value.Id==bookingId));
            return reservation!=null;
        }

        /// <summary>Returns a still-active paid Inn reservation for one care episode.</summary>
        public NpcHealthLodgingReservation NpcHealthRestLodging(string residentId,string careEpisodeId,double now)
        {
            if(!ValidId(residentId) || !NpcHealthCare.ValidEpisodeId(careEpisodeId) || !DefenseTime(now))return null;
            LodgingBooking selected=null;TavernLodging selectedInn=null;
            foreach(TavernLodging inn in state.Lodgings)
                foreach(LodgingBooking booking in inn.Bookings)
                    if(booking.Purpose==LodgingBookingPurpose.NpcHealthRest && booking.GuestId==residentId &&
                        booking.HealthCareId==careEpisodeId && booking.StartsAt<=now && now<booking.EndsAt &&
                        (selected==null || booking.StartsAt>selected.StartsAt))
                    {selected=booking;selectedInn=inn;}
            return NpcHealthLodgingReservation.From(selectedInn==null?0:selectedInn.SettlementId,
                selectedInn==null?0:selectedInn.BuildingKey,selected);
        }

        private static bool SameHealthBooking(LodgingBooking booking,NpcHealthLodgingReservationRequest request,
            double endsAt,string id,string paymentId)
        {
            return booking.Purpose==LodgingBookingPurpose.NpcHealthRest && booking.Id==id &&
                booking.GuestId==request.ResidentId && booking.HealthCareId==request.CareEpisodeId &&
                booking.HealthCareTaskId==request.TaskId && booking.HomeSettlement==request.SettlementId &&
                !booking.Player && booking.PartySize==1 && booking.RoomId==request.RoomId &&
                booking.BedId==request.BedId && booking.StartsAt==request.StartsAt && booking.EndsAt==endsAt &&
                booking.PaidNights==request.Nights && booking.PaidPrice==request.Price &&
                booking.PaymentTransactionId==paymentId;
        }

        private static bool TaskTargetsHealthRestBed(NpcTaskPlan task,int town,int building,string bedId,bool requireReady)
        {
            if(task==null || requireReady && (task.Phase!=NpcTaskPhase.Ready || task.CurrentStepIndex!=0) ||
                task.Steps==null || task.Steps.Count!=1 || task.Steps[0]==null ||
                task.Steps[0].Kind!=NpcActionKind.RestToHeal || task.Steps[0].DurationSeconds<=0)return false;
            NpcActionTarget target=task.Steps[0].Target;
            if(target==null || target.Kind!=NpcActionTargetKind.IndoorPoint || target.SettlementId!=town ||
                target.BuildingKey!=building || target.ContainerId!=bedId || target.ItemId!=null ||
                target.PersonId!=null || target.Quantity!=1 || target.QuotedGold!=0)return false;
            try {NpcTaskContract.Validate(task);}
            catch(ArgumentException) {return false;}
            return true;
        }

        private static bool ValidHealthRestLodgingAuthority(WorldState authority,TavernLodging inn,LodgingBooking booking)
        {
            if(authority==null || inn==null || booking==null || booking.Purpose!=LodgingBookingPurpose.NpcHealthRest ||
                booking.Player || booking.PartySize!=1 || booking.HomeSettlement!=inn.SettlementId ||
                !NpcHealthCare.ValidEpisodeId(booking.HealthCareId) || !ValidId(booking.HealthCareTaskId) ||
                !ValidId(booking.BedId) || booking.PaidPrice<0 || booking.PaidNights<1 ||
                booking.PaidNights>MaximumHealthLodgingNights || booking.PaymentTransactionId!=HealthLodgingPaymentId(booking.HealthCareTaskId) ||
                booking.Id!=HealthLodgingBookingId(booking.HealthCareTaskId) ||
                booking.EndsAt-booking.StartsAt!=booking.PaidNights*HealthLodgingSecondsPerNight ||
                !inn.Rooms.Exists(room=>room.Id==booking.RoomId && !room.NonRentable && room.Beds.Contains(booking.BedId)))return false;
            string taskPrefix="npc-health-care:"+booking.HealthCareId+":attempt:";
            if(!booking.HealthCareTaskId.StartsWith(taskPrefix,StringComparison.Ordinal))return false;
            string revisionText=booking.HealthCareTaskId.Substring(taskPrefix.Length);long revision;
            if(!long.TryParse(revisionText,NumberStyles.None,CultureInfo.InvariantCulture,out revision) || revision<1)return false;
            try
            {
                if(NpcHealthCare.MakeTaskId(new NpcHealthCareRecord {Id=booking.HealthCareId,Revision=revision})!=booking.HealthCareTaskId)return false;
            }
            catch(ArgumentException) {return false;}
            NpcTaskPlan task=authority.NpcTasks==null?null:authority.NpcTasks.Find(value=>value!=null && value.Id==booking.HealthCareTaskId);
            if(task==null || task.ResidentId!=booking.GuestId || booking.StartsAt<task.AcceptedAt ||
                booking.StartsAt>=task.DeadlineAt || !TaskTargetsHealthRestBed(task,inn.SettlementId,inn.BuildingKey,booking.BedId,false))return false;
            return authority.Settlements.Exists(town=>town.SettlementId==booking.HomeSettlement &&
                town.Households.Exists(home=>home.Residents.Exists(person=>person.Id==booking.GuestId)));
        }

        private static string HealthLodgingBookingId(string taskId)
        {return "health-rest-booking:"+taskId;}

        private static string HealthLodgingPaymentId(string taskId)
        {return "health-rest-payment:"+taskId;}

        private static bool ValidId(string value)
        {return !string.IsNullOrWhiteSpace(value) && value.Length<=240;}
    }
}
