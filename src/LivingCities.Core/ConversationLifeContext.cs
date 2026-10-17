using System;
using System.Text;

namespace LivingCities.Core
{
    public sealed class ConversationMoodInputs
    {
        public int Disposition, Hour;
        public bool Grieving, Alarmed, Outdoors, Familiar;
        public string Weather, Activity;
    }

    // Dialogue uses the saved itinerary, never a second invented schedule.
    public static class ConversationLifeContext
    {
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

        public static string RoutineStatus(ResidentRoutine routine, double now, ActivitySnapshot actual,
            double scheduleShiftSeconds, Func<int, string> buildingName)
        {
            if (routine == null) return "No tracked daily itinerary is available.";
            if (buildingName == null || double.IsNaN(now) || double.IsInfinity(now) || now < 0 ||
                double.IsNaN(scheduleShiftSeconds) || double.IsInfinity(scheduleShiftSeconds))
                throw new ArgumentException("Invalid dialogue itinerary context.");
            if (routine.Itinerary == null || routine.Itinerary.Stops == null || routine.Itinerary.Stops.Count == 0)
                return Routine(routine, buildingName);

            var journey = ResidentVisitJourney.Create(routine.ItineraryDepartureSeconds, routine.Itinerary);
            var visits = new System.Collections.Generic.List<ResidentVisitSnapshot>(journey.ScheduledVisits());
            double localNow = now % 86400;
            double scheduledNow = localNow - scheduleShiftSeconds;
            int current = -1;
            if (actual.Activity == Activity.Visit)
                current = visits.FindIndex(visit => visit.BuildingKey == actual.BuildingKey);
            var scheduled = journey.Evaluate(Math.Max(0, scheduledNow));
            bool behind = current >= 0 && scheduledNow > visits[current].LeaveAt + 1;
            var text = new StringBuilder("Current verified activity: ");
            int next = -1;
            if (current >= 0)
            {
                text.Append("currently at ").Append(Place(buildingName(visits[current].BuildingKey), visits[current].Purpose));
                next = current + 1;
            }
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
                text.Append("leave home around ").Append(Clock(routine.ItineraryDepartureSeconds)).Append("; then ");
                for (int index = 0; index < routine.Itinerary.Stops.Count; index++)
                {
                    if (index > 0) text.Append("; then ");
                    var stop = routine.Itinerary.Stops[index];
                    text.Append(Visit(stop.Purpose)).Append(' ').Append(Place(buildingName(stop.BuildingKey), stop.Purpose));
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
            Func<int, string> settlementName, Func<int, string> destinationBuildingName)
        {
            if (trip == null || Math.Floor(now / 86400) != Math.Floor(trip.DepartsAt / 86400)) return null;
            if (settlementName == null || destinationBuildingName == null) throw new ArgumentNullException();
            string town = settlementName(trip.DestinationSettlement);
            if (string.IsNullOrWhiteSpace(town)) town = "another settlement";
            var text = new StringBuilder("Current verified travel activity: ");
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
