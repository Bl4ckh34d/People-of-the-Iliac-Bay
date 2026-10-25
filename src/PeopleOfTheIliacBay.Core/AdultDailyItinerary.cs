using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    public sealed class AdultDailyItineraryOption
    {
        public int BuildingKey, OpenMinute, CloseMinute;
        public ResidentVisitPurpose Purpose;
        public double MinimumDwellSeconds, MaximumDwellSeconds;
    }
    public sealed class AdultDailyItineraryLeg
    {
        public int FromBuildingKey, ToBuildingKey;
        public double WalkSeconds;
        public List<SavedRoutePoint> Route = new List<SavedRoutePoint>();
    }
    public sealed class AdultDailyItineraryRequest
    {
        public int HomeBuildingKey;
        public double DepartureAt, LatestHomeAt, MinimumAwaySeconds;
        public int MinimumStops, MaximumStops;
        public uint Seed;
        public List<AdultDailyItineraryOption> Options = new List<AdultDailyItineraryOption>();
        public List<AdultDailyItineraryLeg> Routes = new List<AdultDailyItineraryLeg>();
    }
    // Builds one deterministic daily outing from runtime-surveyed stops and legs.
    // ResidentVisitJourney remains the only timing and phase evaluator.
    public static class AdultDailyItineraryPlanner
    {
        private struct LegKey : IEquatable<LegKey>
        {
            public int From, To;
            public bool Equals(LegKey other) { return From == other.From && To == other.To; }
            public override bool Equals(object value) { return value is LegKey && Equals((LegKey)value); }
            public override int GetHashCode() { unchecked { return From * 397 ^ To; } }
        }
        private sealed class RandomSource
        {
            private uint state;
            public RandomSource(uint seed) { state = seed == 0 ? 0x9e3779b9u : seed; }
            public uint Next()
            {
                uint x = state; x ^= x << 13; x ^= x >> 17; x ^= x << 5; state = x; return x;
            }
            public int Range(int minimum, int maximumExclusive)
            { return minimum + (int)(Next() % (uint)(maximumExclusive - minimum)); }
            public double Unit() { return Next() / 4294967296.0; }
        }
        public static bool TryCreate(AdultDailyItineraryRequest request, out ResidentDestination destination)
        {
            int searchBudget=512;
            return TryCreate(request,ref searchBudget,out destination);
        }
        public static bool TryCreate(AdultDailyItineraryRequest request,ref int searchBudget,out ResidentDestination destination)
        {
            Validate(request);
            if(searchBudget<0 || searchBudget>512)throw new ArgumentOutOfRangeException(nameof(searchBudget));
            destination = null;
            if(searchBudget==0)return false;
            var routes = new Dictionary<LegKey, AdultDailyItineraryLeg>();
            foreach (var route in request.Routes)
            {
                var key = new LegKey { From = route.FromBuildingKey, To = route.ToBuildingKey };
                if (routes.ContainsKey(key)) throw new ArgumentException("Duplicate adult itinerary route.");
                routes.Add(key, Copy(route));
            }
            var choices = new List<AdultDailyItineraryOption>(request.Options);
            var random = new RandomSource(request.Seed);
            for (int i = choices.Count - 1; i > 0; i--)
            {
                int j = random.Range(0, i + 1); var swap = choices[i]; choices[i] = choices[j]; choices[j] = swap;
            }
            for (int count = request.MaximumStops; count >= request.MinimumStops; count--)
            {
                var selected = new List<AdultDailyItineraryOption>();
                // An impossible long outing must not consume every attempt before
                // the shorter alternatives get a turn. Keep the same total bound.
                int countBudget = Math.Max(1, searchBudget / (count - request.MinimumStops + 1));
                int allocated = countBudget;
                bool found = Search(request, routes, choices, selected, request.HomeBuildingKey, count, ref random, ref countBudget, out destination);
                searchBudget -= allocated - countBudget;
                if (found) return true;
                if (searchBudget == 0) break;
            }
            return false;
        }
        private static bool Search(AdultDailyItineraryRequest request, Dictionary<LegKey, AdultDailyItineraryLeg> routes,
            List<AdultDailyItineraryOption> choices, List<AdultDailyItineraryOption> selected, int current, int target,
            ref RandomSource random, ref int budget, out ResidentDestination result)
        {
            result = null;
            if (budget <= 0) return false;
            if (selected.Count == target)
            {
                budget--;
                if (!routes.ContainsKey(new LegKey { From = current, To = request.HomeBuildingKey })) return false;
                var candidate = Build(request, routes, selected, ref random);
                if (candidate == null) return false;
                result = candidate; return true;
            }
            for (int i = 0; i < choices.Count; i++)
            {
                if (budget <= 0) return false;
                var option = choices[i];
                if (selected.Exists(item => item.BuildingKey == option.BuildingKey) ||
                    !routes.ContainsKey(new LegKey { From = current, To = option.BuildingKey })) continue;
                budget--;
                selected.Add(option);
                if (Search(request, routes, choices, selected, option.BuildingKey, target, ref random, ref budget, out result)) return true;
                selected.RemoveAt(selected.Count - 1);
            }
            return false;
        }
        private static ResidentDestination Build(AdultDailyItineraryRequest request,
            Dictionary<LegKey, AdultDailyItineraryLeg> routes, List<AdultDailyItineraryOption> selected, ref RandomSource random)
        {
            var destination = new ResidentDestination(); int previous = request.HomeBuildingKey;
            foreach (var option in selected)
            {
                var leg = routes[new LegKey { From = previous, To = option.BuildingKey }];
                double dwell = option.MinimumDwellSeconds + random.Unit() * (option.MaximumDwellSeconds - option.MinimumDwellSeconds);
                destination.Stops.Add(new ResidentVisitStop { BuildingKey = option.BuildingKey, Purpose = option.Purpose,
                    OpenMinute = option.OpenMinute, CloseMinute = option.CloseMinute, WalkSeconds = leg.WalkSeconds,
                    DwellSeconds = dwell, Route = CopyPoints(leg.Route) });
                previous = option.BuildingKey;
            }
            var exit = routes[new LegKey { From = previous, To = request.HomeBuildingKey }];
            destination.ExitRoute = CopyPoints(exit.Route); destination.ExitWalkSeconds = exit.WalkSeconds;
            try
            {
                var journey = ResidentVisitJourney.Create(request.DepartureAt, destination);
                if (journey.EndsAt <= request.LatestHomeAt && ActiveOutingSeconds(destination) >= request.MinimumAwaySeconds &&
                    VisitsMeetDwellBounds(journey, selected, destination)) return destination;
            }
            catch (ArgumentException) { return null; }
            return null;
        }
        private static double ActiveOutingSeconds(ResidentDestination destination)
        {
            double total = destination.ExitWalkSeconds;
            foreach (var stop in destination.Stops) total += stop.WalkSeconds + stop.DwellSeconds;
            return total;
        }
        private static bool VisitsMeetDwellBounds(ResidentVisitJourney journey,
            List<AdultDailyItineraryOption> options, ResidentDestination destination)
        {
            var scheduled = new List<ResidentVisitSnapshot>(journey.ScheduledVisits());
            if (scheduled.Count != options.Count || destination.Stops.Count != options.Count) return false;
            for (int i = 0; i < options.Count; i++)
            {
                double dwell = scheduled[i].LeaveAt - scheduled[i].EnterAt;
                // Native Inn opening hours also cover morning travelers. Local
                // social visits must fit the evening without inventing an opening wait.
                if (options[i].Purpose == ResidentVisitPurpose.Social && scheduled[i].EnterAt % 86400 < 16 * 3600)
                    return false;
                if (scheduled[i].BuildingKey != options[i].BuildingKey || dwell + 0.001 < options[i].MinimumDwellSeconds ||
                    dwell - 0.001 > options[i].MaximumDwellSeconds) return false;
            }
            return true;
        }
        private static AdultDailyItineraryLeg Copy(AdultDailyItineraryLeg source)
        {
            if (source == null || source.FromBuildingKey < 0 || source.ToBuildingKey < 0 || source.FromBuildingKey == source.ToBuildingKey ||
                !Positive(source.WalkSeconds) || source.Route == null || source.Route.Count < 2 || source.Route.Count > 10000)
                throw new ArgumentException("Invalid adult itinerary route.");
            var copy = new AdultDailyItineraryLeg { FromBuildingKey = source.FromBuildingKey,
                ToBuildingKey = source.ToBuildingKey, WalkSeconds = source.WalkSeconds, Route = CopyPoints(source.Route) };
            double length = 0;
            for (int i = 1; i < copy.Route.Count; i++)
            {
                double x = copy.Route[i].X - copy.Route[i - 1].X, z = copy.Route[i].Z - copy.Route[i - 1].Z;
                length += Math.Sqrt(x * x + z * z);
            }
            if (!Positive(length)) throw new ArgumentException("Adult itinerary route must have positive length.");
            return copy;
        }
        private static List<SavedRoutePoint> CopyPoints(List<SavedRoutePoint> points)
        {
            if (points == null) throw new ArgumentException("Missing adult itinerary route points.");
            var copy = new List<SavedRoutePoint>(points.Count);
            foreach (var point in points)
            {
                if (point == null || !Finite(point.X) || !Finite(point.Z)) throw new ArgumentException("Invalid adult itinerary route point.");
                copy.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
            }
            return copy;
        }
        private static void Validate(AdultDailyItineraryRequest request)
        {
            if (request == null || request.HomeBuildingKey < 0 || !Finite(request.DepartureAt) || request.DepartureAt < 0 ||
                !Finite(request.LatestHomeAt) || request.LatestHomeAt <= request.DepartureAt ||
                Math.Floor(request.DepartureAt / 86400) != Math.Floor(request.LatestHomeAt / 86400) ||
                !Finite(request.MinimumAwaySeconds) || request.MinimumAwaySeconds <= 0 ||
                request.MinimumAwaySeconds > request.LatestHomeAt - request.DepartureAt ||
                request.MinimumStops < 1 || request.MaximumStops < request.MinimumStops || request.MaximumStops > 6 ||
                request.Options == null || request.Options.Count < request.MinimumStops || request.Options.Count > 32 ||
                request.Routes == null || request.Routes.Count > 2048)
                throw new ArgumentException("Invalid adult daily itinerary request.");
            var buildings = new HashSet<int>();
            foreach (var option in request.Options)
            {
                if (option == null || option.BuildingKey < 0 || option.BuildingKey == request.HomeBuildingKey || !buildings.Add(option.BuildingKey) ||
                    !Enum.IsDefined(typeof(ResidentVisitPurpose), option.Purpose) || option.Purpose == ResidentVisitPurpose.Inn ||
                    option.OpenMinute < 0 || option.OpenMinute >= 1440 ||
                    option.CloseMinute < 0 || option.CloseMinute >= 1440 || !Positive(option.MinimumDwellSeconds) ||
                    !Finite(option.MaximumDwellSeconds) || option.MaximumDwellSeconds < option.MinimumDwellSeconds || option.MaximumDwellSeconds > 12 * 3600)
                    throw new ArgumentException("Invalid adult itinerary option.");
            }
            foreach (var route in request.Routes)
                if (route == null) throw new ArgumentException("Invalid adult itinerary route.");
        }
        private static bool Positive(double value) { return Finite(value) && value > 0; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
