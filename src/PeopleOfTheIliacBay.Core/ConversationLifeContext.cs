using System;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class ConversationMoodInputs
    {
        public int Disposition, Hour;
        public bool Grieving, Alarmed, Outdoors, Familiar;
        public string Weather, Activity;
    }

    public enum DaggerfallHauntingStatus { Inactive, Active }

    // Dialogue uses the saved itinerary, never a second invented schedule.
    public static class ConversationLifeContext
    {
        public static string PersistentMood(int value)
        {
            if (value < -100 || value > 100) throw new ArgumentOutOfRangeException(nameof(value));
            string outlook = value <= -61 ? "very low" : value <= -21 ? "low" :
                value >= 61 ? "very positive" : value >= 21 ? "positive" : "even";
            return "Persistent conversation mood: " + value.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "/100 (" + outlook + "). This outlook is separate from disposition and personality.";
        }

        public static DaggerfallHauntingStatus HauntingStatus(int minuteOfDay)
        {
            if(minuteOfDay<0 || minuteOfDay>=1440)throw new ArgumentOutOfRangeException(nameof(minuteOfDay));
            return minuteOfDay>=19*60 || minuteOfDay<5*60 ?
                DaggerfallHauntingStatus.Active : DaggerfallHauntingStatus.Inactive;
        }

        public static string Haunting(int minuteOfDay)
        {
            return HauntingStatus(minuteOfDay)==DaggerfallHauntingStatus.Active ?
                "King Lysandus's haunting restriction is currently active, from 19:00 to 05:00. You may refuse an outdoor meeting that would take place during this window because you fear the haunting; this does not require refusing ordinary conversation or an indoor plan. " :
                "King Lysandus's haunting restriction is currently inactive. The haunting cannot ground a refusal of an outdoor meeting right now. You may refuse for another current personal or practical reason. Do not claim the streets are universally safe. ";
        }

        public static string Personality(uint identitySeed)
        {
            string warmth = new[] { "warm", "reserved", "guarded", "affectionate" }[Trait(identitySeed, 1, 4)];
            string patience = new[] { "patient", "brisk", "easily wearied", "unhurried" }[Trait(identitySeed, 2, 4)];
            string curiosity = new[] { "inquisitive", "practical", "observant", "self-absorbed" }[Trait(identitySeed, 3, 4)];
            string privacy = new[] { "open", "private", "selective" }[Trait(identitySeed, 4, 3)];
            string manner = new[] { "plainspoken", "courteous", "dry-humored", "earnest" }[Trait(identitySeed, 5, 4)];
            return "Stable personality traits: warmth " + warmth + "; patience " + patience +
                "; curiosity " + curiosity + "; privacy " + privacy + "; manner " + manner + ".";
        }

        public static string Mood(uint identitySeed, int day, ConversationMoodInputs input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            uint daily = StableSeed.Combine(identitySeed, unchecked((uint)day), 0x6d6f6f64u, 1);
            string outlook = input.Disposition <= -60 ? "angry with the player" : input.Disposition <= -20 ? "irritated with the player" :
                input.Disposition >= 60 ? "pleased with the player" :
                new[] { "cheerful", "calm", "thoughtful", "low-spirited" }[daily % 4];
            string energy = new[] { "lively", "tired", "steady", "restless" }[(daily / 5) % 4];
            string tension = new[] { "relaxed", "distracted", "on edge" }[(daily / 19) % 3];
            if (input.Grieving) { outlook = "sad after a personal loss"; energy = "subdued"; }
            if (input.Alarmed) { outlook = "frightened by danger"; tension = "on edge"; }
            var text = new StringBuilder("Current mood: ").Append(outlook).Append(", ").Append(energy).Append(", ").Append(tension);
            if (input.Outdoors)
            {
                string weather = (input.Weather ?? string.Empty).ToLowerInvariant();
                if (weather == "rain") text.Append("; eager to get out of the rain");
                else if (weather == "thunder") text.Append("; unsettled by the storm and seeking shelter");
                else if (weather == "snow") text.Append("; cold and eager for warmth");
                else if (weather == "fog") text.Append("; uneasy in the fog");
                else if (weather == "sunny" && !input.Grieving && !input.Alarmed && input.Disposition > -20)
                    text.Append("; sunshine lifting spirits");
                if (input.Hour >= 20 || input.Hour < 5)
                {
                    if (!input.Familiar) text.Append("; wary of an unfamiliar person after dark and keen to get home");
                    else if (input.Disposition >= 20) text.Append("; more at ease with a familiar face after dark");
                }
            }
            if (!string.IsNullOrWhiteSpace(input.Activity)) text.Append("; occupied with ").Append(input.Activity.Trim());
            return text.Append('.').ToString();
        }

        private static int Trait(uint seed, uint axis, int count)
        { return (int)(StableSeed.Combine(seed, 0x70657273u, axis, 0x6f6e616cu) % (uint)count); }

        public static string EffectiveSchedule(DailySchedule schedule,double now,ActivitySnapshot actual,
            double shift,Func<int,string> buildingName)
        {
            if(actual.Activity==Activity.Recovery) return "Current verified activity: recovery; ordinary work and visits are interrupted. Treatment and return to work require confirmed outcomes.";
            if(schedule==null || buildingName==null || double.IsNaN(now) || double.IsInfinity(now) || now<0 ||
                double.IsNaN(shift) || double.IsInfinity(shift)) throw new ArgumentException("Invalid effective schedule context.");
            var text=new StringBuilder("Current verified activity: ");
            if(actual.Activity==Activity.Visit) text.Append("currently at ").Append(Place(buildingName(actual.BuildingKey),ResidentVisitPurpose.Social));
            else if(actual.Activity==Activity.Travel) text.Append("walking between today's places");
            else text.Append(actual.Activity==Activity.Sleep ? "sleeping at home" : "at home");
            text.Append(". Effective plan today, including committed appointments and only retained ordinary visits: ");
            var visits=schedule.ScheduledVisits();
            foreach(var visit in visits)
            {
                text.Append(Place(buildingName(visit.BuildingKey),visit.Purpose)).Append(" from ")
                    .Append(Clock(visit.EnterAt+shift)).Append(" until ").Append(Clock(visit.LeaveAt+shift)).Append("; ");
            }
            text.Append("return home between separate outings and after the last visit. Past stops are not your current location. ");
            if(shift!=0) text.Append("Physical delays shifted today's plan; arrival times are estimates. ");
            return text.ToString();
        }

        public static ActivitySnapshot ObservedActivity(ActivitySnapshot scheduled,bool outdoors,int building)
        {
            if(scheduled.Activity==Activity.Recovery)
                return new ActivitySnapshot(Activity.Recovery,outdoors?-1:building,scheduled.Position,scheduled.RouteProgress);
            if(outdoors) return new ActivitySnapshot(Activity.Travel,-1,scheduled.Position,scheduled.RouteProgress);
            if(scheduled.BuildingKey==building && scheduled.Activity!=Activity.Travel) return scheduled;
            return new ActivitySnapshot(Activity.Visit,building,scheduled.Position,0);
        }

        public static string RoutineStatus(ResidentRoutine routine, double now, ActivitySnapshot actual,
            double scheduleShiftSeconds, Func<int, string> buildingName)
        {
            if(actual.Activity==Activity.Recovery) return "Current verified activity: recovery; ordinary work and visits are interrupted. Treatment and return to work require confirmed outcomes.";
            if (routine == null) return "No tracked daily itinerary is available.";
            if (buildingName == null || double.IsNaN(now) || double.IsInfinity(now) || now < 0 ||
                double.IsNaN(scheduleShiftSeconds) || double.IsInfinity(scheduleShiftSeconds))
                throw new ArgumentException("Invalid dialogue itinerary context.");
            if (routine.Itinerary == null || routine.Itinerary.Stops == null || routine.Itinerary.Stops.Count == 0)
                return Routine(routine, buildingName);

            var journey = ResidentVisitJourney.Create(routine.ItineraryDepartureSeconds, routine.Itinerary);
            bool composed=routine.AdditionalOutings!=null && routine.AdditionalOutings.Count>0;
            var visits = composed?ResidentRoutineDay.ScheduledVisits(routine):
                new System.Collections.Generic.List<ResidentVisitSnapshot>(journey.ScheduledVisits());
            double localNow = now % 86400;
            double scheduledNow = localNow - scheduleShiftSeconds;
            int current = -1;
            if (actual.Activity == Activity.Visit)
            {
                current = visits.FindIndex(visit => visit.BuildingKey == actual.BuildingKey);
                if(composed)
                {
                    int latest=visits.FindLastIndex(visit=>visit.BuildingKey==actual.BuildingKey && visit.EnterAt<=scheduledNow+1);
                    if(latest>=0)current=latest;
                }
            }
            var scheduled = journey.Evaluate(Math.Max(0, scheduledNow));
            if(composed)
            {
                scheduled.VisitIndex=-1;int offset=0;
                foreach(var outing in ResidentRoutineDay.CopyOutings(routine))
                {
                    var child=ResidentVisitJourney.Create(outing.DepartureSeconds,outing.Itinerary);
                    if(scheduledNow>=outing.DepartureSeconds && scheduledNow<child.EndsAt)
                    {
                        scheduled=child.Evaluate(scheduledNow);
                        if(scheduled.VisitIndex>=0)scheduled.VisitIndex+=offset;
                        break;
                    }
                    offset+=outing.Itinerary.Stops.Count;
                }
            }
            bool behind = current >= 0 && scheduledNow > visits[current].LeaveAt + 1;
            var text = new StringBuilder("Current verified activity: ");
            int next = -1;
            if (current >= 0)
            {
                text.Append("currently at ").Append(Place(buildingName(visits[current].BuildingKey), visits[current].Purpose));
                next = current + 1;
            }
            else if(actual.Activity==Activity.Visit)
                text.Append("currently at ").Append(Place(buildingName(actual.BuildingKey),ResidentVisitPurpose.Social))
                    .Append("; this actual stop is outside the original itinerary");
            else if (actual.Activity == Activity.Travel && scheduled.VisitIndex >= 0 &&
                scheduled.VisitIndex < visits.Count)
            {
                text.Append("traveling toward ").Append(Place(buildingName(visits[scheduled.VisitIndex].BuildingKey),
                    visits[scheduled.VisitIndex].Purpose));
                next = scheduled.VisitIndex;
            }
            else if (actual.Activity == Activity.Home && scheduledNow < routine.ItineraryDepartureSeconds)
            {
                text.Append("at home before today's outing");
                next = 0;
            }
            else if(actual.Activity==Activity.Home && composed && visits.Exists(visit=>visit.EnterAt>scheduledNow))
            {
                text.Append("at home between today's outings");
                next=visits.FindIndex(visit=>visit.EnterAt>scheduledNow);
            }
            else
                text.Append(actual.Activity == Activity.Travel ? "returning home" : "at home after today's outing");

            if (next >= 0 && next < visits.Count)
            {
                text.Append("; next planned stop: ").Append(Visit(visits[next].Purpose)).Append(' ')
                    .Append(Place(buildingName(visits[next].BuildingKey), visits[next].Purpose));
                if (behind || scheduleShiftSeconds != 0 || visits[next].EnterAt + scheduleShiftSeconds < localNow)
                    text.Append("; arrival time uncertain because movement ran late");
                else text.Append("; planned arrival around ").Append(Clock(visits[next].EnterAt + scheduleShiftSeconds));
                for (int index = next + 1; index < visits.Count; index++)
                    text.Append("; later ").Append(Visit(visits[index].Purpose)).Append(' ')
                        .Append(Place(buildingName(visits[index].BuildingKey), visits[index].Purpose));
                text.Append("; then return home");
            }
            else if (current >= 0) text.Append("; then return home");
            return text.Append(". Planned times may shift after physical delays or interruptions.").ToString();
        }

        public static string Routine(ResidentRoutine routine, Func<int, string> buildingName)
        {
            if (routine == null) return "No tracked daily itinerary is available.";
            if (buildingName == null) throw new ArgumentNullException(nameof(buildingName));
            var text = new StringBuilder("Actual recurring plan today: ");
            if (routine.Itinerary != null && routine.Itinerary.Stops != null && routine.Itinerary.Stops.Count > 0)
            {
                // Describing a plan does not execute it or validate route geometry.
                var outings=new System.Collections.Generic.List<ResidentDailyOuting>{new ResidentDailyOuting{
                    DepartureSeconds=routine.ItineraryDepartureSeconds,Itinerary=routine.Itinerary}};
                if(routine.AdditionalOutings!=null)outings.AddRange(routine.AdditionalOutings);
                for(int outingIndex=0;outingIndex<outings.Count;outingIndex++)
                {
                    if(outingIndex>0)text.Append("; then return home; later ");
                    var outing=outings[outingIndex];
                    text.Append("leave home around ").Append(Clock(outing.DepartureSeconds)).Append("; then ");
                    for (int index = 0; index < outing.Itinerary.Stops.Count; index++)
                    {
                        if (index > 0) text.Append("; then ");
                        var stop = outing.Itinerary.Stops[index];
                        text.Append(Visit(stop.Purpose)).Append(' ').Append(Place(buildingName(stop.BuildingKey), stop.Purpose));
                    }
                }
                text.Append("; then return home. Stops can change after interruptions.");
            }
            else if (routine.DestinationKey >= 0)
            {
                text.Append("visit ").Append(Place(buildingName(routine.DestinationKey), ResidentVisitPurpose.Shop));
                text.Append(" during opening hours, then return home.");
            }
            else text.Append("stay near home; no outside visit is scheduled.");
            return text.ToString();
        }

        public static string NativeDuty(string workName, string homeName, bool atWork)
        {
            if (string.IsNullOrWhiteSpace(workName) && string.IsNullOrWhiteSpace(homeName))
                return "No tracked daily itinerary is available.";
            var text = new StringBuilder("Known daily obligations: ");
            if (!string.IsNullOrWhiteSpace(workName)) text.Append(atWork ? "currently working at " : "works at ").Append(workName.Trim());
            if (!string.IsNullOrWhiteSpace(homeName))
            {
                if (!string.IsNullOrWhiteSpace(workName)) text.Append("; ");
                text.Append("home is ").Append(homeName.Trim());
            }
            text.Append(". No further stops or departure time are tracked.");
            return text.ToString();
        }

        public static string NativeStaticDuty(string verifiedRole)
        {
            string role = string.IsNullOrWhiteSpace(verifiedRole) ? "unknown; do not infer one from appearance or name" : verifiedRole.Trim();
            return "Verified native static actor role: " + role + ". This native role classification does not replace authored quest persona or explicit quest facts. No personal daily itinerary or current travel task is tracked. " +
                "Your authored placement does not prove that you are walking, visiting a market, shopping, looking for food, or performing now. " +
                "Describe only a separately verified task, quest fact, or observed activity as current movement or performance.";
        }

        public static string Trip(ResidentTrip trip, double now, Func<int, string> settlementName,
            Func<int, string> destinationBuildingName)
        {
            if (trip == null || Math.Floor(now / 86400) != Math.Floor(trip.DepartsAt / 86400) ||
                trip.Road != null && trip.Road.Count >= 2 && now >= trip.HomeAt) return null;
            if (settlementName == null || destinationBuildingName == null) throw new ArgumentNullException();
            string town = settlementName(trip.DestinationSettlement);
            var text = new StringBuilder(now < trip.DepartsAt ? "Actual travel plan today: leave for " :
                "Actual travel plan today: traveling to ");
            text.Append(string.IsNullOrWhiteSpace(town) ? "another settlement" : town.Trim());
            if (now < trip.DepartsAt) text.Append(" around ").Append(Clock(trip.DepartsAt));
            if (trip.Destination != null && trip.Destination.Stops != null)
                foreach (var stop in trip.Destination.Stops)
                    text.Append("; then ").Append(Visit(stop.Purpose)).Append(' ')
                        .Append(Place(destinationBuildingName(stop.BuildingKey), stop.Purpose));
            text.Append("; then travel home. Actual travel may be delayed.");
            return text.ToString();
        }

        public static string TripStatus(ResidentTrip trip, double now, ResidentTripSnapshot actual,
            Func<int, string> settlementName, Func<int, string> destinationBuildingName,ActivitySnapshot? observed=null)
        {
            if (trip == null) return null;
            if (settlementName == null || destinationBuildingName == null) throw new ArgumentNullException();
            string lodging=trip.LodgingPlanContext(now,destinationBuildingName);
            if(lodging!=null)return "Recorded lodging plan: "+lodging+" Observed position and activity determine whether you are physically there or asleep.";
            if(Math.Floor(now / 86400) != Math.Floor(trip.DepartsAt / 86400))return null;
            string town = settlementName(trip.DestinationSettlement);
            if (string.IsNullOrWhiteSpace(town)) town = "another settlement";
            var text = new StringBuilder("Current verified travel activity: ");
            if(observed.HasValue)
            {
                if(observed.Value.Activity==Activity.Travel && actual.Phase==ResidentTripPhase.Visiting && actual.BuildingKey>=0)
                {
                    // A timetable may cross the arrival boundary before the body
                    // enters the doorway. Never turn that into a claimed visit.
                    text.Append("physically outdoors, heading toward ").Append(Place(destinationBuildingName(actual.BuildingKey),actual.VisitPurpose))
                        .Append("; not arrived inside yet. ");
                    actual.BuildingKey=-1;actual.VisitPhase=ResidentVisitPhase.Walking;
                }
                else if(observed.Value.Activity==Activity.Visit && actual.Phase==ResidentTripPhase.Visiting)
                    actual.BuildingKey=observed.Value.BuildingKey;
            }
            if (actual.Phase == ResidentTripPhase.Returning || actual.Phase == ResidentTripPhase.ReturningHome ||
                actual.Phase == ResidentTripPhase.WaitingHomeGate)
                return text.Append("returning home from ").Append(town).Append('.').ToString();
            if (actual.Phase == ResidentTripPhase.Home && now >= trip.DepartsAt)
                return text.Append("returned home from ").Append(town).Append('.').ToString();
            if (actual.Phase == ResidentTripPhase.Home && now < trip.DepartsAt)
                text.Append("at home before departing for ").Append(town).Append(" around ").Append(Clock(trip.DepartsAt));
            else if (actual.Phase == ResidentTripPhase.Visiting && actual.BuildingKey >= 0)
                text.Append("currently at ").Append(Place(destinationBuildingName(actual.BuildingKey), actual.VisitPurpose))
                    .Append(" in ").Append(town);
            else if (actual.Phase == ResidentTripPhase.Visiting)
                text.Append("traveling within ").Append(town);
            else text.Append("traveling to ").Append(town);

            int first = 0;
            if (actual.Phase == ResidentTripPhase.Visiting && actual.BuildingKey >= 0 &&
                trip.Destination != null && trip.Destination.Stops != null)
            {
                int current = trip.Destination.Stops.FindIndex(stop => stop.BuildingKey == actual.BuildingKey);
                if (current >= 0) first = current + 1;
            }
            if (trip.Destination != null && trip.Destination.Stops != null)
                for (int index = first; index < trip.Destination.Stops.Count; index++)
                {
                    var stop = trip.Destination.Stops[index];
                    text.Append(index == first ? "; next planned stop: " : "; later ")
                        .Append(Visit(stop.Purpose)).Append(' ')
                        .Append(Place(destinationBuildingName(stop.BuildingKey), stop.Purpose));
                }
            return text.Append("; then travel home. Planned visits may be delayed or interrupted.").ToString();
        }

        private static string Clock(double seconds)
        {
            int minute = (int)Math.Floor(Math.Max(0, seconds) / 60) % 1440;
            return (minute / 60).ToString("00") + ":" + (minute % 60).ToString("00");
        }

        private static string Visit(ResidentVisitPurpose purpose)
        {
            switch (purpose)
            {
                case ResidentVisitPurpose.Shop: return "shop at";
                case ResidentVisitPurpose.Temple: return "visit";
                case ResidentVisitPurpose.Guild: return "visit";
                case ResidentVisitPurpose.Castle: return "go to";
                case ResidentVisitPurpose.Inn: return "stay at";
                default: return "spend time at";
            }
        }

        private static string Place(string name, ResidentVisitPurpose purpose)
        {
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            switch (purpose)
            {
                case ResidentVisitPurpose.Shop: return "a shop";
                case ResidentVisitPurpose.Temple: return "a temple";
                case ResidentVisitPurpose.Guild: return "a guild hall";
                case ResidentVisitPurpose.Castle: return "the castle";
                case ResidentVisitPurpose.Inn: return "an inn";
                default: return "a public place";
            }
        }
    }
}
