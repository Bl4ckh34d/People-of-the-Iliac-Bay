using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Native acceptance for the rendered local-patron loop. A real scheduled resident
    /// is observed frame by frame from the inn doorway, through the keeper and room stops,
    /// and back to the surveyed exit. The fixture changes only the in-memory plan schedule.
    /// </summary>
    public static class InnSocialMovementProbe
    {
        private static readonly BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

        private static object Field(object instance, string name)
        {
            var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(instance.GetType().FullName, name);
            return field.GetValue(instance);
        }

        private static float Distance(Vector3 a, Vector3 b)
        {
            return Vector3.Distance(a, b);
        }

        private static string PointKey(ShopVisitSnapshot sample)
        {
            return Math.Round(sample.Position.X, 2) + ":" + Math.Round(sample.Position.Y, 2) + ":" + Math.Round(sample.Position.Z, 2);
        }

        private static object FindEligiblePlan(StreetPopulation streets, int innKey)
        {
            var plans = (IEnumerable)typeof(StreetPopulation).GetField("plans", InstancePrivate).GetValue(streets);
            var store = (WorldStateStore)typeof(StreetPopulation).GetField("worldStore", InstancePrivate).GetValue(streets);
            var familyOwns = typeof(StreetPopulation).GetMethod("RuntimeFamilyOwnsDay", InstancePrivate);
            var journeys = (IDictionary)typeof(StreetPopulation).GetField("shopJourneys", InstancePrivate).GetValue(streets);
            var departures = (IDictionary)typeof(StreetPopulation).GetField("shopDepartures", InstancePrivate).GetValue(streets);
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            foreach (var plan in plans)
            {
                var resident = (Resident)Field(plan, "Resident");
                if (resident != null && resident.IsAlive && !resident.IsQuestProtected && resident.NativeIdentity == null &&
                    resident.AgeGroup != ResidentAgeGroup.Child && resident.HomeKey != innKey)
                {
                    // This is an ordinary patron fixture. Do not replace a
                    // parent schedule owned by the real childcare coordinator.
                    if ((bool)familyOwns.Invoke(streets, new object[] { plan, now })) continue;
                    var state = ((DailySchedule)Field(plan, "Schedule")).EvaluateSeconds(now);
                    if (state.Activity != Activity.Home && state.Activity != Activity.Sleep || streets.FindActor(resident.Id) ||
                        journeys.Contains(resident.Id) || departures.Contains(resident.Id) || store.GetObservedVisit(resident.Id) != null) continue;
                    var household = store.ReadNpcHouseholdKnowledge(resident.Id);
                    bool parent = false;
                    if (household != null) foreach (var child in household.Children) if (child.IsAlive) parent = true;
                    if (!parent) return plan;
                }
            }
            return null;
        }

        private static int CurrentMinute(double now)
        {
            return (int)((now % 86400) / 60);
        }

        private static void ClearJourney(StreetPopulation streets, string id)
        {
            var journeys = (IDictionary)typeof(StreetPopulation).GetField("shopJourneys", InstancePrivate).GetValue(streets);
            var departures = (IDictionary)typeof(StreetPopulation).GetField("shopDepartures", InstancePrivate).GetValue(streets);
            if (journeys.Contains(id)) journeys.Remove(id);
            if (departures.Contains(id)) departures.Remove(id);
        }

        public static IEnumerator Run(PeopleOfTheIliacBayMod adapter, StreetPopulation streets, DaggerfallInterior room)
        {
            if (!adapter || !streets || !room || !GameManager.Instance.IsPlayerInside ||
                GameManager.Instance.PlayerEnterExit.Interior != room)
                throw new InvalidOperationException("Inn movement probe requires the player inside its surveyed inn.");

            var plan = FindEligiblePlan(streets, room.EntryDoor.buildingKey);
            if (plan == null) throw new InvalidOperationException("Inn movement probe lacks an eligible local adult fixture.");

            var planType = plan.GetType();
            var resident = (Resident)Field(plan, "Resident");
            var scheduleField = planType.GetField("Schedule");
            var routine = (ResidentRoutine)Field(plan, "Routine");
            var originalSchedule = scheduleField.GetValue(plan);
            int originalDestination = routine.DestinationKey;
            var interiorGeometry = (InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry", InstancePrivate).GetValue(streets);
            if (interiorGeometry == null || !interiorGeometry.EntryGround.HasValue)
                throw new InvalidOperationException("Inn movement probe lacks a surveyed doorway ground point.");

            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            int minute = CurrentMinute(now);
            int opens = minute + 1;
            if (opens > 1400)
                throw new InvalidOperationException("Inn movement fixture needs at least twenty-five waking minutes left in the current game day.");
            int closes = opens + 20;
            float gameRate = DaggerfallUnity.Instance.WorldTime.TimeScale;
            if (gameRate <= 0 || float.IsNaN(gameRate) || float.IsInfinity(gameRate))
                throw new InvalidOperationException("Inn movement fixture requires an advancing native game clock.");
            var schedule = DailySchedule.DayVisit(resident.HomeKey, room.EntryDoor.buildingKey,
                new[] { new RoutePoint(0, 0), new RoutePoint(1, 0) }, opens, closes, 15,
                ResidentMovement.WalkSpeed, gameRate, 0u, Math.Max(0, opens - 2), 1439);
            double start = 0, end = 0;
            bool hasWindow = false;
            for (int candidate = opens; candidate < closes; candidate++)
                if (schedule.TryVisitWindow(candidate * 60, out start, out end)) { hasWindow = true; break; }
            double dayBase = Math.Floor(now / 86400) * 86400;
            start += dayBase;
            end += dayBase;
            if (!hasWindow || end <= now || end - start < 60)
                throw new InvalidOperationException("Inn movement fixture could not create a future visit window.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_SOCIAL_FIXTURE: resident="+resident.Id+"; now="+now+
                "; start="+start+"; end="+end+"; timeScale="+gameRate+"; householdOwner=false; seededEarliestSlot=true");

            // The synthetic visit must also exist in the authoritative routine
            // ledger: physical completion persists an observed visit against it.
            var store = (WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store", InstancePrivate).GetValue(adapter);
            int settlementId = GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory().MapID;
            var originalRoutines = store.GetRoutines(settlementId) ?? new TownRoutineState { SettlementId = settlementId };
            var probeRoutines = store.GetRoutines(settlementId) ?? new TownRoutineState { SettlementId = settlementId };
            probeRoutines.Residents.RemoveAll(item => item.ResidentId == resident.Id);
            probeRoutines.Residents.Add(new ResidentRoutine {
                ResidentId = resident.Id, DestinationKey = room.EntryDoor.buildingKey,
                OpenMinute = opens, CloseMinute = closes,
                Route = new List<SavedRoutePoint> { new SavedRoutePoint { X = 0, Z = 0 }, new SavedRoutePoint { X = 1, Z = 0 } }
            });
            var originalObserved = store.GetObservedVisit(resident.Id);
            try
            {
                scheduleField.SetValue(plan, schedule);
                routine.DestinationKey = room.EntryDoor.buildingKey;
                store.RemoveObservedVisit(resident.Id);
                store.SetRoutines(probeRoutines);
                typeof(StreetPopulation).GetField("shopVisitDay", InstancePrivate).SetValue(streets, -1d);
                ClearJourney(streets, resident.Id);

                var deadline = Time.realtimeSinceStartup + 48f;
                var journeys = (Dictionary<string, ShopVisitJourney>)typeof(StreetPopulation).GetField("shopJourneys", InstancePrivate).GetValue(streets);
                ShopVisitJourney journey = null;
                ResidentActor actor = null;
                while (Time.realtimeSinceStartup < deadline)
                {
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                    journeys.TryGetValue(resident.Id, out journey);
                    actor = streets.FindActor(resident.Id);
                    if (journey != null && actor) break;
                    yield return null;
                }
                if (journey == null || !actor)
                    throw new InvalidOperationException("Scheduled inn patron did not produce a live runtime journey and actor within 48 seconds: resident="+
                        resident.Id+"; journey="+(journey!=null)+"; actor="+(bool)actor+"; now="+
                        DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()+"; start="+start+"; end="+end);
                if (!journey.Purchases) throw new InvalidOperationException("Inn patron did not begin with the keeper counter stop.");

                var occupancies = new List<ShopOccupancy>(journey.Occupancies());
                int expectedSocialStops = Math.Max(0, occupancies.Count - 1); // First stop is the keeper counter.
                if (expectedSocialStops < 2)
                    throw new InvalidOperationException("Runtime inn journey does not contain two distinct social stops.");

                Vector3 entry = interiorGeometry.EntryGround.Value + Vector3.up;
                Vector3 previous = actor.transform.position;
                if (Distance(previous, entry) > 1.25f)
                    throw new InvalidOperationException("Local patron did not start at the surveyed inn entrance.");
                float maximumStep = 0, totalTravel = 0, approachTravel = 0, postCounterTravel = 0, exitTravel = 0;
                var reachedStops = new HashSet<string>();
                bool counterReached = false, counterPassed = false, exited = false;
                float lastDoorDistance = float.MaxValue;
                ShopVisitPhase previousPhase = ShopVisitPhase.Walking;
                double previousSeconds = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                deadline = Time.realtimeSinceStartup + Math.Max(135f,(float)((end-previousSeconds)/gameRate)+60f);
                while (previousSeconds < end && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    actor = streets.FindActor(resident.Id);
                    double seconds = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                    if (!actor)
                    {
                        if(seconds < end || Distance(previous,entry)>1.1f)
                            throw new InvalidOperationException("Local patron vanished before reaching its scheduled doorway exit.");
                        exited=true; previousSeconds=seconds; break;
                    }
                    var sample = journey.Evaluate(seconds);
                    var expected = room.transform.TransformPoint(new Vector3((float)sample.Position.X,
                        (float)sample.Position.Y, (float)sample.Position.Z)) + Vector3.up;
                    float step = Distance(previous, actor.transform.position);
                    maximumStep = Math.Max(maximumStep, step);
                    totalTravel += step;
                    if (sample.Phase == ShopVisitPhase.Walking)
                    {
                        approachTravel += step;
                        if (counterPassed) postCounterTravel += step;
                    }
                    if (sample.Phase == ShopVisitPhase.Haggling && Distance(actor.transform.position, expected) <= 0.9f)
                        counterReached = true;
                    if (sample.Phase == ShopVisitPhase.Browsing && Distance(actor.transform.position, expected) <= 0.9f)
                    {
                        reachedStops.Add(PointKey(sample));
                        if (counterReached) counterPassed = true;
                    }
                    if (sample.Phase == ShopVisitPhase.Leaving)
                    {
                        exitTravel += step;
                        lastDoorDistance = Distance(actor.transform.position, entry);
                    }
                    if (previousPhase == ShopVisitPhase.Haggling && sample.Phase != ShopVisitPhase.Haggling)
                        counterPassed = counterReached;
                    previous = actor.transform.position;
                    previousPhase = sample.Phase;
                    previousSeconds = seconds;
                }
                if (previousSeconds < end)
                    throw new InvalidOperationException("Inn patron visit exceeded its bounded runtime window.");
                if (!counterReached || reachedStops.Count < 2 || approachTravel < 0.75f || postCounterTravel < 1f || maximumStep > 0.75f)
                    throw new InvalidOperationException("Rendered inn patron missed counter/social movement criteria: counter=" + counterReached +
                        "; stops=" + reachedStops.Count + "; approachM=" + approachTravel + "; betweenStopsM=" + postCounterTravel +
                        "; maxFrameM=" + maximumStep + "; totalM=" + totalTravel);

                var departureStart = actor ? actor.transform.position : previous;
                float previousExitDistance = Distance(departureStart, entry);
                lastDoorDistance = previousExitDistance;
                deadline = Time.realtimeSinceStartup + 30f;
                while (!exited && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    actor = streets.FindActor(resident.Id);
                    if (!actor)
                    {
                        exited = true;
                        break;
                    }
                    float step = Distance(previous, actor.transform.position);
                    maximumStep = Math.Max(maximumStep, step);
                    exitTravel += step;
                    previous = actor.transform.position;
                    double seconds = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                    if (seconds >= end)
                    {
                        float distanceToEntry = Distance(actor.transform.position, entry);
                        if (distanceToEntry < previousExitDistance) previousExitDistance = distanceToEntry;
                        lastDoorDistance = distanceToEntry;
                    }
                }
                if (!exited || exitTravel < 0.75f || maximumStep > 0.75f || previousExitDistance > 4f || lastDoorDistance > 1.1f)
                    throw new InvalidOperationException("Local inn patron did not walk out through the surveyed doorway: exited=" + exited +
                        "; exitM=" + exitTravel + "; nearestDoorM=" + previousExitDistance + "; lastDoorM=" + lastDoorDistance +
                        "; maxFrameM=" + maximumStep);

                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_SOCIAL_MOVEMENT_PASS: resident=" + resident.Id + "; building=" + room.EntryDoor.buildingKey +
                    "; keeper reached; socialStops=" + reachedStops.Count + "; approachM=" + approachTravel +
                    "; betweenStopsM=" + postCounterTravel + "; exitM=" + exitTravel + "; maxFrameM=" + maximumStep +
                    "; rendered route observed frame by frame.");
            }
            finally
            {
                store.RemoveObservedVisit(resident.Id);
                store.SetRoutines(originalRoutines);
                if (originalObserved != null) store.StoreObservedVisit(originalObserved);
                scheduleField.SetValue(plan, originalSchedule);
                routine.DestinationKey = originalDestination;
                ClearJourney(streets, resident.Id);
                typeof(StreetPopulation).GetField("shopVisitDay", InstancePrivate).SetValue(streets, -1d);
            }
        }
    }
}
