using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class ObservedVisitLeg
    {
        public List<GuardPoint3> Route = new List<GuardPoint3>();
        public double DwellSeconds;
        public ShopVisitPhase AfterPhase;
        public string SeatId;
    }

    public struct ObservedVisitSnapshot
    {
        public GuardPoint3 Position;
        public ShopVisitPhase Phase;
        public int LegIndex;
        public bool Completed;
    }

    [Serializable]
    public sealed class ObservedVisit
    {
        private const int MaximumLegs = 32;
        private const int MaximumPoints = 10000;
        private const double MaximumDuration = 7 * 86400d;
        private const double ConnectedTolerance = 0.001;

        public string ResidentId;
        public string HouseholdDayId,HouseholdSourceFingerprint;
        public string HouseholdSeatId;
        public int SettlementId, BuildingKey;
        public double ObservedAt, PlannedEndAt, ResumeUntil, SecondsPerMetre;
        public List<ObservedVisitLeg> Legs = new List<ObservedVisitLeg>();
        public NpcInnConsumptionReceipt InnConsumption;
        public bool CounterPurchaseResolved;

        public double EndsAt
        {
            get { return ObservedAt + Duration(Legs, SecondsPerMetre); }
        }
        public bool AffectsRoutineDuring(double startsAt,double endsAt)
        {
            if(!Finite(startsAt) || !Finite(endsAt) || startsAt<0 || endsAt<=startsAt)
                throw new ArgumentOutOfRangeException(nameof(endsAt));
            double end=EndsAt;
            double resumes=Math.Max(end,ResumeUntil+(end-PlannedEndAt));
            return startsAt<resumes && endsAt>ObservedAt;
        }

        public static ObservedVisit Copy(ObservedVisit source)
        {
            if(source!=null)HouseholdInnVisitProvenance.Validate(source.HouseholdDayId,source.HouseholdSourceFingerprint);
            if(source!=null && source.HouseholdSeatId!=null && (source.HouseholdDayId==null ||
                string.IsNullOrWhiteSpace(source.HouseholdSeatId) || source.HouseholdSeatId.Length>256))
                throw new ArgumentException("Invalid shared Inn chair intent.");
            if (source == null || string.IsNullOrWhiteSpace(source.ResidentId) || source.ResidentId.Length > 256 ||
                source.SettlementId < 0 || source.BuildingKey < 0 || !Finite(source.ObservedAt) || source.ObservedAt < 0 ||
                !Finite(source.PlannedEndAt) || source.PlannedEndAt < 0 || !Finite(source.ResumeUntil) ||
                source.ResumeUntil < source.PlannedEndAt ||
                !Finite(source.SecondsPerMetre) || source.SecondsPerMetre <= 0 || source.Legs == null ||
                source.Legs.Count < 1 || source.Legs.Count > MaximumLegs)
                throw new ArgumentException("Invalid observed visit identity, time, rate, or legs.");

            var copy = new ObservedVisit
            {
                ResidentId = source.ResidentId,
                HouseholdDayId=source.HouseholdDayId,HouseholdSourceFingerprint=source.HouseholdSourceFingerprint,
                HouseholdSeatId=source.HouseholdSeatId,
                CounterPurchaseResolved=source.CounterPurchaseResolved,
                SettlementId = source.SettlementId,
                BuildingKey = source.BuildingKey,
                ObservedAt = source.ObservedAt,
                PlannedEndAt = source.PlannedEndAt,
                ResumeUntil = source.ResumeUntil,
                SecondsPerMetre = source.SecondsPerMetre,
                Legs = new List<ObservedVisitLeg>(source.Legs.Count),
            };
            copy.InnConsumption = NpcInnConsumption.CopyReceipt(source.InnConsumption, copy);

            int totalPoints = 0;
            ObservedVisitLeg previous = null;
            for (int i = 0; i < source.Legs.Count; i++)
            {
                var leg = source.Legs[i];
                if (leg == null || leg.Route == null || leg.Route.Count < 1 ||
                    leg.SeatId != null && (leg.SeatId.Length > 256 || string.IsNullOrWhiteSpace(leg.SeatId) || leg.AfterPhase != ShopVisitPhase.Browsing) ||
                    !Finite(leg.DwellSeconds) || leg.DwellSeconds < 0 || !ValidPhase(leg.AfterPhase, i == source.Legs.Count - 1) ||
                    i == source.Legs.Count - 1 && leg.DwellSeconds != 0)
                    throw new ArgumentException("Invalid observed visit leg.");

                totalPoints += leg.Route.Count;
                if(copy.HouseholdSeatId!=null && leg.SeatId!=null && leg.SeatId!=copy.HouseholdSeatId)
                    throw new ArgumentException("Shared Inn remaining chair differs from its retained seat intent.");
                if (totalPoints > MaximumPoints) throw new ArgumentException("Observed visit has too many route points.");

                var route = new List<GuardPoint3>(leg.Route.Count);
                foreach (var point in leg.Route)
                {
                    if (point == null || !Coordinate(point.X) || !Coordinate(point.Y) || !Coordinate(point.Z))
                        throw new ArgumentException("Invalid observed visit route point.");
                    route.Add(new GuardPoint3 { X = point.X, Y = point.Y, Z = point.Z });
                }
                if (previous != null && Distance(previous.Route[previous.Route.Count - 1], route[0]) > ConnectedTolerance)
                    throw new ArgumentException("Observed visit legs are disconnected.");

                copy.Legs.Add(new ObservedVisitLeg
                {
                    Route = route,
                    DwellSeconds = leg.DwellSeconds,
                    AfterPhase = leg.AfterPhase,
                    SeatId = leg.SeatId,
                });
                previous = copy.Legs[copy.Legs.Count - 1];
            }

            double duration = Duration(copy.Legs, copy.SecondsPerMetre);
            if (!Finite(duration) || duration > MaximumDuration || !Finite(copy.ObservedAt + duration))
                throw new ArgumentException("Observed visit duration exceeds seven days.");
            return copy;
        }

        public ObservedVisitSnapshot Evaluate(double now)
        {
            if (!Finite(now) || now < ObservedAt) throw new ArgumentOutOfRangeException(nameof(now));
            double elapsed = now - ObservedAt;
            for (int legIndex = 0; legIndex < Legs.Count; legIndex++)
            {
                var leg = Legs[legIndex];
                double travel = RouteLength(leg.Route) * SecondsPerMetre;
                if (elapsed < travel)
                {
                    return new ObservedVisitSnapshot
                    {
                        Position = Along(leg.Route, elapsed / SecondsPerMetre),
                        Phase = legIndex == Legs.Count - 1 ? ShopVisitPhase.Leaving : ShopVisitPhase.Walking,
                        LegIndex = legIndex,
                        Completed = false,
                    };
                }
                elapsed -= travel;
                if (elapsed < leg.DwellSeconds)
                {
                    return new ObservedVisitSnapshot
                    {
                        Position = Clone(leg.Route[leg.Route.Count - 1]),
                        Phase = leg.AfterPhase,
                        LegIndex = legIndex,
                        Completed = false,
                    };
                }
                elapsed -= leg.DwellSeconds;
            }

            var lastLeg = Legs[Legs.Count - 1];
            return new ObservedVisitSnapshot
            {
                Position = Clone(lastLeg.Route[lastLeg.Route.Count - 1]),
                Phase = ShopVisitPhase.Complete,
                LegIndex = Legs.Count - 1,
                Completed = true,
            };
        }

        // Called once by the versioned save migration. Keep the current physical
        // position and remaining activity time; only walking uses the new rate.
        public void MigrateClockRate(double now, double previousRate, double currentRate)
        {
            double ratio = ClockRateMigration.RescaleDuration(1, previousRate, currentRate);
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (ratio == 1 || now >= EndsAt) return;
            if (now >= ObservedAt)
            {
                Legs = RemainingLegs(now);
                ObservedAt = now;
            }
            SecondsPerMetre *= ratio;
        }

        public List<ObservedVisitLeg> RemainingLegs(double now)
        {
            if (!Finite(now) || now < ObservedAt) throw new ArgumentOutOfRangeException(nameof(now));
            var remaining = new List<ObservedVisitLeg>();
            double elapsed = now - ObservedAt;
            for (int legIndex = 0; legIndex < Legs.Count; legIndex++)
            {
                var leg = Legs[legIndex];
                double length = RouteLength(leg.Route);
                double travel = length * SecondsPerMetre;
                if (elapsed < travel)
                {
                    remaining.Add(new ObservedVisitLeg
                    {
                        Route = ClipRoute(leg.Route, elapsed / SecondsPerMetre),
                        DwellSeconds = leg.DwellSeconds,
                        AfterPhase = leg.AfterPhase,
                        SeatId = leg.SeatId,
                    });
                    AppendRemaining(remaining, legIndex + 1);
                    return remaining;
                }

                elapsed -= travel;
                if (elapsed < leg.DwellSeconds)
                {
                    var endpoint = Clone(leg.Route[leg.Route.Count - 1]);
                    remaining.Add(new ObservedVisitLeg
                    {
                        Route = new List<GuardPoint3> { endpoint },
                        DwellSeconds = leg.DwellSeconds - elapsed,
                        AfterPhase = leg.AfterPhase,
                        SeatId = leg.SeatId,
                    });
                    AppendRemaining(remaining, legIndex + 1);
                    return remaining;
                }
                elapsed -= leg.DwellSeconds;
            }
            return remaining;
        }

        private void AppendRemaining(List<ObservedVisitLeg> target, int firstLeg)
        {
            for (int i = firstLeg; i < Legs.Count; i++) target.Add(CopyLeg(Legs[i]));
        }

        private static ObservedVisitLeg CopyLeg(ObservedVisitLeg source)
        {
            var route = new List<GuardPoint3>(source.Route.Count);
            foreach (var point in source.Route) route.Add(Clone(point));
            return new ObservedVisitLeg { Route = route, DwellSeconds = source.DwellSeconds, AfterPhase = source.AfterPhase, SeatId = source.SeatId };
        }

        private static List<GuardPoint3> ClipRoute(List<GuardPoint3> route, double distance)
        {
            var result = new List<GuardPoint3>();
            if (route.Count == 1)
            {
                result.Add(Clone(route[0]));
                return result;
            }

            for (int i = 1; i < route.Count; i++)
            {
                double segment = Distance(route[i - 1], route[i]);
                if (segment > 0 && distance < segment)
                {
                    double fraction = distance / segment;
                    var a = route[i - 1];
                    var b = route[i];
                    result.Add(new GuardPoint3
                    {
                        X = a.X + (b.X - a.X) * fraction,
                        Y = a.Y + (b.Y - a.Y) * fraction,
                        Z = a.Z + (b.Z - a.Z) * fraction,
                    });
                    for (int j = i; j < route.Count; j++) result.Add(Clone(route[j]));
                    return result;
                }
                distance -= segment;
            }

            result.Add(Clone(route[route.Count - 1]));
            return result;
        }

        private static GuardPoint3 Along(List<GuardPoint3> route, double distance)
        {
            for (int i = 1; i < route.Count; i++)
            {
                var a = route[i - 1];
                var b = route[i];
                double segment = Distance(a, b);
                if (segment > 0 && distance < segment)
                {
                    double fraction = distance / segment;
                    return new GuardPoint3
                    {
                        X = a.X + (b.X - a.X) * fraction,
                        Y = a.Y + (b.Y - a.Y) * fraction,
                        Z = a.Z + (b.Z - a.Z) * fraction,
                    };
                }
                distance -= segment;
            }
            return Clone(route[route.Count - 1]);
        }

        private static double Duration(List<ObservedVisitLeg> legs, double secondsPerMetre)
        {
            double duration = 0;
            foreach (var leg in legs)
                duration += RouteLength(leg.Route) * secondsPerMetre + leg.DwellSeconds;
            return duration;
        }

        private static double RouteLength(List<GuardPoint3> route)
        {
            double length = 0;
            for (int i = 1; i < route.Count; i++) length += Distance(route[i - 1], route[i]);
            return length;
        }

        private static bool ValidPhase(ShopVisitPhase phase, bool final)
        {
            if (final) return phase == ShopVisitPhase.Complete;
            return phase == ShopVisitPhase.Browsing || phase == ShopVisitPhase.Haggling;
        }

        private static GuardPoint3 Clone(GuardPoint3 point)
        { return new GuardPoint3 { X = point.X, Y = point.Y, Z = point.Z }; }

        private static double Distance(GuardPoint3 a, GuardPoint3 b)
        {
            double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        private static bool Coordinate(double value)
        { return Finite(value) && Math.Abs(value) <= 1000000; }

        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
