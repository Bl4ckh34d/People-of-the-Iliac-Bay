using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private struct DailyRouteKey : IEquatable<DailyRouteKey>
        {
            public int From, To;
            public bool Equals(DailyRouteKey other) { return From == other.From && To == other.To; }
            public override bool Equals(object value) { return value is DailyRouteKey && Equals((DailyRouteKey)value); }
            public override int GetHashCode() { unchecked { return From * 397 ^ To; } }
        }

        private sealed class DailyRoute
        {
            public List<SavedRoutePoint> Points;
            public double WalkSeconds;
        }

        private readonly Dictionary<DailyRouteKey, DailyRoute> dailyRouteCache = new Dictionary<DailyRouteKey, DailyRoute>();
        private readonly LinkedList<DailyRouteKey> dailyRouteCacheOrder = new LinkedList<DailyRouteKey>();
        private readonly Dictionary<DailyRouteKey, LinkedListNode<DailyRouteKey>> dailyRouteCacheNodes =
            new Dictionary<DailyRouteKey, LinkedListNode<DailyRouteKey>>();
        private const int MaximumDailyRouteCacheEntries = 512;

        private void CacheDailyRoute(DailyRouteKey key, DailyRoute route)
        {
            LinkedListNode<DailyRouteKey> node;
            if (dailyRouteCacheNodes.TryGetValue(key, out node))
            {
                dailyRouteCache[key] = route;
                dailyRouteCacheOrder.Remove(node);
                dailyRouteCacheOrder.AddLast(node);
                return;
            }
            if (dailyRouteCache.Count >= MaximumDailyRouteCacheEntries)
            {
                var oldest = dailyRouteCacheOrder.First;
                if (oldest != null)
                {
                    dailyRouteCache.Remove(oldest.Value);
                    dailyRouteCacheNodes.Remove(oldest.Value);
                    dailyRouteCacheOrder.RemoveFirst();
                }
            }
            dailyRouteCache[key] = route;
            dailyRouteCacheNodes[key] = dailyRouteCacheOrder.AddLast(key);
        }

        private static bool TryGetDailyPurpose(DFLocation.BuildingTypes type, out ResidentVisitPurpose purpose)
        {
            if (type == DFLocation.BuildingTypes.Temple) { purpose = ResidentVisitPurpose.Temple; return true; }
            if (type == DFLocation.BuildingTypes.GuildHall) { purpose = ResidentVisitPurpose.Guild; return true; }
            if (type == DFLocation.BuildingTypes.Tavern) { purpose = ResidentVisitPurpose.Social; return true; }
            if (type == DFLocation.BuildingTypes.Palace) { purpose = ResidentVisitPurpose.Castle; return true; }
            switch (type)
            {
                case DFLocation.BuildingTypes.Alchemist:
                case DFLocation.BuildingTypes.Armorer:
                case DFLocation.BuildingTypes.Bookseller:
                case DFLocation.BuildingTypes.ClothingStore:
                case DFLocation.BuildingTypes.FurnitureStore:
                case DFLocation.BuildingTypes.GemStore:
                case DFLocation.BuildingTypes.GeneralStore:
                case DFLocation.BuildingTypes.PawnShop:
                case DFLocation.BuildingTypes.WeaponSmith:
                    purpose = ResidentVisitPurpose.Shop;
                    return true;
                default:
                    purpose = ResidentVisitPurpose.Shop;
                    return false;
            }
        }

        private static uint DailyCandidateRank(uint seed, int homeKey, int buildingKey, ResidentVisitPurpose purpose)
        {
            unchecked
            {
                uint value = seed ^ ((uint)homeKey * 0x9e3779b9u) ^ ((uint)buildingKey * 0x85ebca6bu) ^ ((uint)purpose * 0xc2b2ae35u);
                value ^= value >> 16; value *= 0x7feb352du;
                value ^= value >> 15; value *= 0x846ca68bu;
                value ^= value >> 16;
                return value;
            }
        }

        private List<AdultDailyItineraryOption> SelectDailyItineraryOptions(List<BuildingSummary> buildings,
            Resident resident, int homeKey,int maximumCandidates=0)
        {
            var ranked = new List<AdultDailyItineraryOption>();
            foreach (var building in buildings)
            {
                if (building.buildingKey == homeKey || !geometry.Entrances.ContainsKey(building.buildingKey)) continue;
                ResidentVisitPurpose purpose;
                if (!TryGetDailyPurpose(building.BuildingType, out purpose)) continue;
                int type = (int)building.BuildingType;
                DailyServiceWindow serviceWindow;
                if(!DailyServiceHours.TryFromNativeHours(PlayerActivate.openHours[type],PlayerActivate.closeHours[type],out serviceWindow))continue;
                int opens=serviceWindow.OpenMinute,closes=serviceWindow.CloseMinute;
                double minimumDwell, maximumDwell;
                if (purpose == ResidentVisitPurpose.Shop) { minimumDwell = 20 * 60; maximumDwell = 50 * 60; }
                else if (purpose == ResidentVisitPurpose.Social) { minimumDwell = 45 * 60; maximumDwell = 120 * 60; }
                else { minimumDwell = 20 * 60; maximumDwell = 60 * 60; }
                ranked.Add(new AdultDailyItineraryOption { BuildingKey = building.buildingKey, Purpose = purpose,
                    OpenMinute = opens, CloseMinute = closes, MinimumDwellSeconds = minimumDwell, MaximumDwellSeconds = maximumDwell });
            }

            int limit = 4;
            uint variety = DailyCandidateRank(resident.AppearanceSeed, homeKey, homeKey, ResidentVisitPurpose.Shop);
            if (variety % 32 == 0) limit = 6;
            else if (variety % 5 == 0) limit = 5;
            if(maximumCandidates>0)limit=Math.Min(24,maximumCandidates);
            if (ranked.Count <= limit) return SortDailyOptions(ranked, resident.AppearanceSeed, homeKey);

            // Reserve one candidate per available service class before filling
            // remaining slots. Each resident still gets a stable, varied subset.
            var selected = new List<AdultDailyItineraryOption>(limit);
            var categoryOrder = new[] { ResidentVisitPurpose.Shop, ResidentVisitPurpose.Social,
                ResidentVisitPurpose.Temple, ResidentVisitPurpose.Guild, ResidentVisitPurpose.Castle };
            int firstCategory = (int)(variety % (uint)categoryOrder.Length);
            for (int offset = 0; offset < categoryOrder.Length && selected.Count < limit; offset++)
            {
                var purpose = categoryOrder[(firstCategory + offset) % categoryOrder.Length];
                AdultDailyItineraryOption best = null;
                uint bestRank = uint.MaxValue;
                foreach (var option in ranked)
                {
                    if (option.Purpose != purpose || selected.Exists(item => item.BuildingKey == option.BuildingKey)) continue;
                    uint rank = DailyCandidateRank(resident.AppearanceSeed, homeKey, option.BuildingKey, purpose);
                    if (best == null || rank < bestRank) { best = option; bestRank = rank; }
                }
                if (best != null) selected.Add(best);
            }
            foreach (var option in SortDailyOptions(ranked, resident.AppearanceSeed, homeKey))
            {
                if (selected.Count >= limit) break;
                if (!selected.Exists(item => item.BuildingKey == option.BuildingKey)) selected.Add(option);
            }
            return SortDailyOptions(selected, resident.AppearanceSeed, homeKey);
        }

        private static List<AdultDailyItineraryOption> SortDailyOptions(List<AdultDailyItineraryOption> options,
            uint seed, int homeKey)
        {
            options.Sort((a, b) => {
                uint rankA = DailyCandidateRank(seed, homeKey, a.BuildingKey, a.Purpose);
                uint rankB = DailyCandidateRank(seed, homeKey, b.BuildingKey, b.Purpose);
                int order = rankA.CompareTo(rankB);
                return order != 0 ? order : a.BuildingKey.CompareTo(b.BuildingKey);
            });
            return options;
        }

        private DailyRoute GetDailyRoute(int from, int to)
        {
            var key = new DailyRouteKey { From = from, To = to };
            DailyRoute route;
            if (dailyRouteCache.TryGetValue(key, out route))
            {
                TouchDailyRoute(key);
                return route;
            }
            var reverseKey = new DailyRouteKey { From = to, To = from };
            if (dailyRouteCache.TryGetValue(reverseKey, out route))
            {
                TouchDailyRoute(reverseKey);
                var reversed = ReverseDailyRoute(route);
                CacheDailyRoute(key, reversed);
                return reversed;
            }
            return null;
        }

        private void TouchDailyRoute(DailyRouteKey key)
        {
            LinkedListNode<DailyRouteKey> node;
            if (!dailyRouteCacheNodes.TryGetValue(key, out node)) return;
            dailyRouteCacheOrder.Remove(node);
            dailyRouteCacheOrder.AddLast(node);
        }

        private static DailyRoute ReverseDailyRoute(DailyRoute route)
        {
            var reversed = new DailyRoute { Points = new List<SavedRoutePoint>(), WalkSeconds = route.WalkSeconds };
            for (int i = route.Points.Count - 1; i >= 0; i--)
                reversed.Points.Add(new SavedRoutePoint { X = route.Points[i].X, Z = route.Points[i].Z });
            return reversed;
        }

        private DailyRoute GetDailyRoute(int from, int to, Dictionary<DailyRouteKey, DailyRoute> residentRoutes)
        {
            var key = new DailyRouteKey { From = from, To = to };
            DailyRoute route;
            if (residentRoutes.TryGetValue(key, out route))
            {
                CacheDailyRoute(key, route);
                return route;
            }
            if (residentRoutes.TryGetValue(new DailyRouteKey { From = to, To = from }, out route))
            {
                route = ReverseDailyRoute(route);
                residentRoutes[key] = route;
                CacheDailyRoute(key, route);
                return route;
            }
            route = GetDailyRoute(from, to);
            if (route != null) residentRoutes[key] = route;
            return route;
        }

        private DailyRoute SurveyDailyRoute(int from, int to, List<SavedRoutePoint> points)
        {
            long started = RuntimeTiming.Start();
            try
            {
                var route = new DailyRoute { Points = points ?? new List<SavedRoutePoint>(), WalkSeconds = 0 };
                double length = 0;
                for (int i = 0; i < route.Points.Count; i++)
                {
                    if (i == 0) continue;
                    double dx = route.Points[i].X - route.Points[i - 1].X, dz = route.Points[i].Z - route.Points[i - 1].Z;
                    length += Math.Sqrt(dx * dx + dz * dz);
                }
                if (route.Points.Count >= 2 && length > 0)
                {
                    route.WalkSeconds = length * DaggerfallUnity.Instance.WorldTime.TimeScale / ResidentMovement.WalkSpeed;
                    // Keep exact doorway controls and the subdivisions used by
                    // the sliced corridor survey. Curves are already compact.
                }
                else route.Points.Clear();
                CacheDailyRoute(new DailyRouteKey { From = from, To = to }, route);
                return route;
            }
            finally { RuntimeTiming.End("daily-route-survey", started); }
        }

        private static bool CompactDailyRoutineRoutes(ResidentRoutine routine)
        {
            bool changed = SimplifySavedRoute(routine.Route);
            if (routine.Itinerary == null) return changed;
            foreach(var itinerary in ResidentRoutineDay.Itineraries(routine))
            {
                foreach (var stop in itinerary.Stops)changed |= SimplifySavedRoute(stop.Route);
                changed |= SimplifySavedRoute(itinerary.ExitRoute);
            }
            return changed;
        }

        private static bool SimplifySavedRoute(List<SavedRoutePoint> route)
        {
            if (route == null || route.Count < 3) return false;
            var compact = SavedRouteSimplifier.Simplify(route);
            if (compact.Count >= route.Count) return false;
            route.Clear();
            route.AddRange(compact);
            return true;
        }

        private static double DailyDepartureSeconds(Resident resident, int wake, int sleep)
        {
            uint rank = DailyCandidateRank(resident.AppearanceSeed, resident.HomeKey, resident.HomeKey, ResidentVisitPurpose.Guild);
            double departure = 8 * 3600 + rank % (2 * 3600 + 1);
            double earliest = wake * 3600.0;
            double latest = Math.Max(earliest, sleep * 3600.0 - 1);
            return Math.Min(Math.Max(departure, earliest), latest);
        }

        private double DailyReturnDeadline(int sleep)
        {
            double deadline=sleep*3600d-1;
            if(population.SettlementId==1291010263 && worldStore.RestrictHauntingRoutines(population.SettlementId,
                GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()))
                deadline=Math.Min(deadline,18*3600+45*60);
            return deadline;
        }

        private ResidentRoutine RefreshDailyServiceHours(ResidentRoutine source)
        {
            try {return DailyServiceHours.Refresh(source,key=>
            {
                BuildingSummary summary;
                if(!directory || !directory.GetBuildingSummary(key,out summary))return null;
                ResidentVisitPurpose purpose;
                if(!TryGetDailyPurpose(summary.BuildingType,out purpose))return null;
                int type=(int)summary.BuildingType;DailyServiceWindow window;
                return DailyServiceHours.TryFromNativeHours(PlayerActivate.openHours[type],PlayerActivate.closeHours[type],out window)?window:null;
            });}
            catch(ArgumentException error)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DAILY_HOURS_DEFERRED: resident="+source.ResidentId+"; "+error.Message);
                return null;
            }
        }

        private static void KeepFirstStopAsLegacyRoute(ResidentRoutine routine, ResidentDestination itinerary)
        {
            if (routine == null || itinerary == null || itinerary.Stops == null || itinerary.Stops.Count == 0) return;
            var first = itinerary.Stops[0];
            routine.DestinationKey = first.BuildingKey;
            routine.RouteGeometryVersion=OutdoorRoutineMigration.CenterlineVersion;
            routine.PersonalLaneVersion=0;
            routine.OpenMinute = first.OpenMinute;
            routine.CloseMinute = first.CloseMinute;
            routine.Route = new List<SavedRoutePoint>();
            foreach (var point in first.Route)
                routine.Route.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
        }

        private void ReplacePlanForItinerary(Resident resident, ResidentRoutine routine, uint surnameSeed, int wake, int sleep)
        {
            var existing = plans.Find(plan => plan.Resident.Id == resident.Id);
            if (existing != null) plans.Remove(existing);
            AddPlan(resident, routine, surnameSeed, wake, sleep);
        }
    }
}
