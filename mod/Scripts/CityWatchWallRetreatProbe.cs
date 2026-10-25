using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Controlled native acceptance probe for the City Watch 2% wall refuge path.
    /// Run only in a disposable Daggerfall fixture process; never saves the game.
    /// </summary>
    internal static class CityWatchWallRetreatProbe
    {
        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string ProbeName = "People of the Iliac Bay disposable City Watch wall retreat target";

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, GuardPopulation guards, uint worldSeed)
        {
            Require(host && guards && host.GetComponent<GuardPopulation>() == guards,
                "City Watch wall probe requires the active host-owned GuardPopulation.");
            var game = GameManager.Instance;
            Require(game != null, "City Watch wall probe requires the active game manager.");
            bool originalGodMode = game.PlayerEntity.GodMode;
            try
            {
                DFLocation city;
                if (!DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall", "Daggerfall", out city))
                    throw new InvalidOperationException("City Watch wall probe could not resolve Daggerfall.");
                DFPosition pixel = MapsFile.LongitudeLatitudeToMapPixel(
                    city.MapTableData.Longitude, city.MapTableData.Latitude);
                game.PlayerEntity.GodMode = true;
                game.StreamingWorld.TeleportToCoordinates(pixel.X, pixel.Y);

                float deadline = Time.realtimeSinceStartup + 90;
                while ((!game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer ||
                    !game.PlayerGPS.HasCurrentLocation ||
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId != city.MapTableData.MapId) &&
                    Time.realtimeSinceStartup < deadline)
                    yield return null;
                Require(game.PlayerGPS.HasCurrentLocation &&
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId == city.MapTableData.MapId &&
                    game.StreamingWorld.IsReady && !game.StreamingWorld.IsRepositioningPlayer,
                    "City Watch wall probe timed out while loading Daggerfall.");
                yield return new WaitForSecondsRealtime(2);

                guards = host.GetComponent<GuardPopulation>();
                deadline = Time.realtimeSinceStartup + 240;
                float nextReadinessDiagnostic = Time.realtimeSinceStartup + 15;
                FieldInfo setupPostsField = RequireField(typeof(GuardPopulation), "setupPosts");
                FieldInfo setupEntrancesField = RequireField(typeof(GuardPopulation), "setupEntrances");
                FieldInfo setupSurveyField = RequireField(typeof(GuardPopulation), "setupSurvey");
                FieldInfo setupDirectoryField = RequireField(typeof(GuardPopulation), "setupDirectory");
                while (guards && (!guards.OwnsExterior || guards.Roster == null ||
                    guards.Roster.SettlementId != city.MapTableData.MapId) &&
                    Time.realtimeSinceStartup < deadline)
                {
                    if (Time.realtimeSinceStartup >= nextReadinessDiagnostic)
                    {
                        nextReadinessDiagnostic = Time.realtimeSinceStartup + 15;
                        var streets = host.GetComponent<StreetPopulation>();
                        var posts = setupPostsField.GetValue(guards) as IList;
                        var entrances = setupEntrancesField.GetValue(guards) as TownGeometry;
                        var setupDirectory = setupDirectoryField.GetValue(guards) as BuildingDirectory;
                        string hostStatus = host.TestStatus().Replace('\n', '|');
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CITYWATCH_WALL_WAIT: elapsed=" +
                            (240 - Math.Max(0, deadline - Time.realtimeSinceStartup)).ToString("F0", CultureInfo.InvariantCulture) +
                            "; streets-ready=" + (streets && streets.Ready) +
                            "; own-exterior=" + guards.OwnsExterior +
                            "; roster=" + (guards.Roster == null ? "null" : guards.Roster.SettlementId.ToString(CultureInfo.InvariantCulture)) +
                            "; setup-directory=" + (setupDirectory ? setupDirectory.MapID.ToString(CultureInfo.InvariantCulture) : "null") +
                            "; posts=" + (posts == null ? "null" : posts.Count.ToString(CultureInfo.InvariantCulture)) +
                            "; entrances=" + (entrances == null ? "null" : entrances.Entrances.Count.ToString(CultureInfo.InvariantCulture)) +
                            "; survey-pending=" + (setupSurveyField.GetValue(guards) != null) +
                            "; host=" + hostStatus);
                    }
                    yield return null;
                }
                Require(guards && guards.OwnsExterior && guards.Roster != null &&
                    guards.Roster.SettlementId == city.MapTableData.MapId,
                    "Host guard controller did not attach to the loaded Daggerfall Town23 fixture.");
                yield return RunAtCurrentCity(host, guards, worldSeed);
            }
            finally
            {
                game.PlayerEntity.GodMode = originalGodMode;
            }
        }

        private static IEnumerator RunAtCurrentCity(PeopleOfTheIliacBayMod host, GuardPopulation guards, uint worldSeed)
        {
            Require(host && guards && host.GetComponent<GuardPopulation>() == guards,
                "City Watch wall probe requires the active host-owned GuardPopulation.");
            var game = GameManager.Instance;
            Require(game != null && game.PlayerGPS.HasCurrentLocation && !game.PlayerEnterExit.IsPlayerInside &&
                !GameManager.IsGamePaused && guards.OwnsExterior,
                "City Watch wall probe requires the active exterior settlement fixture.");
            Require(guards.Roster != null && guards.Roster.SettlementId == game.PlayerGPS.CurrentLocation.MapTableData.MapId,
                "City Watch wall probe requires the current settlement roster.");

            FieldInfo actorsField = RequireField(typeof(GuardPopulation), "actors");
            FieldInfo worldStoreField = RequireField(typeof(GuardPopulation), "worldStore");
            FieldInfo directoryField = RequireField(typeof(GuardPopulation), "directory");
            FieldInfo geometryField = RequireField(typeof(GuardPopulation), "geometry");
            FieldInfo refugeGeometryField = RequireField(typeof(GuardPopulation), "refugeGeometry");
            FieldInfo retryField = RequireField(typeof(GuardPopulation), "cityWatchRetreatRetryAt");
            FieldInfo refugeCacheField = RequireField(typeof(GuardPopulation), "refugeInteriors");
            MethodInfo viewMethod = typeof(GuardPopulation).GetMethod("View", InstancePrivate);
            Require(viewMethod != null, "GuardPopulation.View is unavailable.");

            var liveRoster = guards.Roster;
            var rosterBackup = GuardRoster.CopyValidated(liveRoster);
            bool restoreRoster = true;
            bool originalEnabled = guards.enabled;
            var retryBefore = CopyDictionary((IDictionary)retryField.GetValue(guards));
            var refugeCache = (IDictionary)refugeCacheField.GetValue(guards);
            var refugeKeysBefore = new HashSet<object>();
            foreach (object key in refugeCache.Keys) refugeKeysBefore.Add(key);
            object originalRefugeGeometry = refugeGeometryField.GetValue(guards);
            var actorMap = (Dictionary<string, GuardActor>)actorsField.GetValue(guards);
            GuardActor probeActor = null;
            string probeGuardId = null;
            GameObject targetObject = null;
            var randomState = UnityEngine.Random.state;
            uint dfRandomSeed = DFRandom.Seed;

            try
            {
                Require(worldStoreField.GetValue(guards) is WorldStateStore,
                    "Host GuardPopulation has no live WorldStateStore.");
                var liveStore = (WorldStateStore)worldStoreField.GetValue(guards);
                Require(liveStore.WorldSeed == worldSeed,
                    "Probe world seed differs from the active host's saved world seed.");

                GuardRecord candidate = null;
                var gameTime = DaggerfallUnity.Instance.WorldTime.Now;
                double now = (double)gameTime.ToSeconds() + (double)gameTime.Second - Math.Floor(gameTime.Second);
                foreach (var guard in liveRoster.Guards)
                {
                    if (!guard.IsAlive || guard.Duty != GuardDuty.Posted || guard.IncidentId != null ||
                        guard.Refuge != null || guard.Interior != null || guard.PostRecall != null ||
                        actorMap.ContainsKey(guard.Id)) continue;
                    candidate = guard;
                    break;
                }
                Require(candidate != null,
                    "No unmaterialized posted City Watch guard is available for an isolated native fixture.");
                probeGuardId = candidate.Id;

                int roll;
                ulong targetLoadId = FindEligibleTargetLoadId(worldSeed, probeGuardId, out roll);
                var directory = (BuildingDirectory)directoryField.GetValue(guards);
                var geometry = (TownGeometry)geometryField.GetValue(guards);
                Require(directory && geometry != null && directory.MapID == liveRoster.SettlementId,
                    "Host guard controller has no current Town23 navigation geometry.");

                var initial = liveRoster.Position(probeGuardId, now);
                Vector3 targetGround = geometry.ScenePoint(new RoutePoint(initial.X, initial.Z)) + Vector3.right * 2.5f;
                guards.enabled = false;

                targetObject = SpawnInertTarget(targetGround, targetLoadId);
                var target = targetObject.GetComponent<DaggerfallEntityBehaviour>();
                Require(target && target.Entity != null && targetObject.GetComponent<DaggerfallEnemy>().LoadID == targetLoadId,
                    "Disposable target lost its stable nonzero native LoadID.");

                // Give the target one frame with every native AI component disabled. Its
                // serializer was removed before activation, so this fixture cannot be saved.
                yield return null;

                Vector3 targetLocal = directory.transform.InverseTransformPoint(targetObject.transform.position);
                var route = new List<SavedRoutePoint>
                {
                    new SavedRoutePoint { X = initial.X, Z = initial.Z },
                    new SavedRoutePoint { X = targetLocal.x, Z = targetLocal.z }
                };
                liveRoster.Dispatch("citywatch-wall-probe:" + probeGuardId, now,
                    new Dictionary<string, List<SavedRoutePoint>> { { probeGuardId, route } }, 1, 2.8,
                    DaggerfallUnity.Instance.WorldTime.TimeScale);
                liveRoster.Engage(probeGuardId, now);
                candidate.HealthFraction = 0.25;
                probeActor = (GuardActor)viewMethod.Invoke(guards, new object[] { candidate, now });
                Require(probeActor && !probeActor.IsInterior && probeActor.GuardId == probeGuardId,
                    "Host controller did not materialize the selected City Watch guard.");

                var body = probeActor.GetComponent<DaggerfallEntityBehaviour>();
                var native = probeActor.GetComponent<DaggerfallEnemy>();
                var motor = probeActor.GetComponent<EnemyMotor>();
                var senses = probeActor.GetComponent<EnemySenses>();
                var character = probeActor.GetComponent<CharacterController>();
                Require(body && native && motor && senses && character && native.LoadID == 0,
                    "Managed guard fixture is missing its native combat body or stable GuardId identity.");
                body.Entity.SetHealth(Math.Max(1, (int)Math.Floor(body.Entity.MaxHealth * 0.25f)), true);
                senses.Target = null;
                senses.DetectedTarget = false;
                motor.IsHostile = false;
                motor.enabled = true;
                character.enabled = true;

                var binding = probeActor.GetComponent<NativeNpcCombatRoleBinding>();
                Require(binding != null, "Host View did not bind the managed City Watch native role.");

                // EnemyMotor.Start and the role binding's LateUpdate both participate in
                // callback installation. Wait for their actual handoff, not one frame of order.
                float handlerDeadline = Time.realtimeSinceStartup + 2;
                while (probeActor && motor && motor.enabled && binding &&
                    !binding.OwnsTakeActionHandler && Time.realtimeSinceStartup < handlerDeadline)
                    yield return null;
                if (!binding || !binding.OwnsTakeActionHandler)
                {
                    var callback = motor ? motor.TakeActionHandler : null;
                    string callbackInfo = callback == null ? "null" :
                        (callback.Method.DeclaringType == null ? "null" : callback.Method.DeclaringType.FullName) + "." + callback.Method.Name +
                        ";target=" + (callback.Target == null ? "null" : callback.Target.GetType().FullName);
                    Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_CITYWATCH_WALL_CALLBACK_TIMEOUT: actor-alive=" + (probeActor != null) +
                        "; motor-alive=" + (motor != null) + "; motor-enabled=" + (motor && motor.enabled) +
                        "; character-alive=" + (character != null) + "; character-enabled=" + (character && character.enabled) +
                        "; binding-alive=" + (binding != null) + "; binding-enabled=" + (binding && binding.enabled) +
                        "; owns=" + (binding && binding.OwnsTakeActionHandler) + "; callback=" + callbackInfo);
                }
                Require(probeActor && motor && character && binding,
                    "Managed City Watch actor was destroyed while initializing its combat callback.");
                motor.enabled = false;
                Require(binding.OwnsTakeActionHandler,
                    "Native role binding did not wrap EnemyMotor.TakeAction within two seconds.");
                senses.Target = target;
                senses.DetectedTarget = true;
                motor.MakeEnemyHostileToAttacker(target);
                PropertyInfo combatReady = typeof(GuardActor).GetProperty("CombatReady",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Require(combatReady != null && combatReady.GetSetMethod(true) != null,
                    "GuardActor combat-ready state is unavailable for the controlled fixture.");
                combatReady.GetSetMethod(true).Invoke(probeActor, new object[] { true });
                motor.enabled = true;

                Vector3 beforeCallback = probeActor.transform.position;
                int nativeCallsBefore = binding.NativeDelegatedActionCount;
                guards.enabled = originalEnabled;
                Require(guards.OwnsExterior, "Host guard controller lost current exterior ownership.");
                motor.TakeActionHandler();
                guards.enabled = false;

                Require(binding.NativeDelegatedActionCount == nativeCallsBefore,
                    "Eligible City Watch callback delegated to native combat instead of accepting wall refuge.");
                Require(Vector3.Distance(beforeCallback, probeActor.transform.position) < 0.02f,
                    "City Watch retreat callback directly moved the native controller.");

                var saved = liveRoster.Guards.Find(guard => guard.Id == probeGuardId);
                Require(saved != null && saved.IsAlive && saved.Duty == GuardDuty.Refuge &&
                    saved.IncidentId == null && saved.Refuge != null,
                    "Eligible native City Watch callback did not persist a refuge assignment.");
                BuildingSummary refugeBuilding;
                Require(directory.GetBuildingSummary(saved.Refuge.BuildingKey, out refugeBuilding) &&
                    refugeBuilding.BuildingType == DaggerfallConnect.DFLocation.BuildingTypes.Town23,
                    "City Watch retreat selected a building other than verified Town23.");
                Require(saved.Refuge.ReleasedAt.HasValue,
                    "City Watch refuge did not save its calendar release time.");
                int refugeBuildingKey = saved.Refuge.BuildingKey;
                Require(probeActor.HasActiveCombatRefuge(body),
                    "Native callback scheduled refuge but did not hold combat refuge ownership.");

                double exteriorLength = RouteLength(saved.Refuge.Exterior);
                double interiorLength = RouteLength(saved.Refuge.Interior);
                double returnLength = RouteLength(saved.Refuge.ReturnExterior);
                double secondsPerMetre = saved.Refuge.SecondsPerMetre;
                double shelteredAt = now + (exteriorLength + interiorLength) * secondsPerMetre;
                Require(saved.Refuge.ReleasedAt.Value >= shelteredAt + 3600,
                    "City Watch refuge released before one sheltered hour elapsed.");

                var serialized = SaveLoadManager.Serialize(typeof(GuardRoster), liveRoster);
                var restored = GuardRoster.CopyValidated((GuardRoster)SaveLoadManager.Deserialize(typeof(GuardRoster), serialized));
                var persisted = restored.Guards.Find(guard => guard.Id == probeGuardId);
                Require(persisted != null && persisted.Refuge != null && persisted.Refuge.BuildingKey == saved.Refuge.BuildingKey &&
                    persisted.Refuge.ReleasedAt == saved.Refuge.ReleasedAt && persisted.Duty == GuardDuty.Refuge,
                    "City Watch Town23 route or release did not survive roster serialization.");

                double walkGameSeconds = Math.Min(exteriorLength * secondsPerMetre * 0.25, 2.0 * secondsPerMetre);
                walkGameSeconds = Math.Max(walkGameSeconds, Math.Min(0.2 * secondsPerMetre, exteriorLength * secondsPerMetre * 0.5));
                double approachAt = now + walkGameSeconds;
                Vector3 beforeApproach = probeActor.transform.position;
                probeActor.Place(approachAt);
                Vector3 approachMotion = probeActor.transform.position - beforeApproach;
                approachMotion.y = 0;
                var expectedApproach = geometry.ScenePoint(new RoutePoint(
                    liveRoster.Position(probeGuardId, approachAt).X, liveRoster.Position(probeGuardId, approachAt).Z));
                Require(approachMotion.magnitude >= 0.05f &&
                    Vector3.Distance(new Vector3(probeActor.transform.position.x, expectedApproach.y, probeActor.transform.position.z), expectedApproach) < 0.15f,
                    "GuardActor did not follow saved Town23 exterior route under roster control.");

                double returnAt = saved.Refuge.ReleasedAt.Value + (interiorLength + 0.5) * secondsPerMetre;
                var returnPhase = saved.Refuge.Evaluate(returnAt);
                Require(returnPhase.Phase == RefugePhase.Returning,
                    "Released City Watch refuge did not enter its saved return route.");
                Vector3 beforeReturn = probeActor.transform.position;
                probeActor.Place(returnAt);
                Require(Vector3.Distance(beforeReturn, probeActor.transform.position) >= 0.05f,
                    "GuardActor did not resume physical placement along the released return route.");

                double completeAt = saved.Refuge.ReleasedAt.Value +
                    (interiorLength + returnLength + 1) * secondsPerMetre;
                liveRoster.Advance(completeAt);
                var completed = liveRoster.Guards.Find(guard => guard.Id == probeGuardId);
                Require(completed != null && completed.Duty == GuardDuty.Posted && completed.Refuge == null &&
                    completed.IncidentId == null,
                    "City Watch did not finish refuge return as a posted guard.");
                Require(!probeActor.HasActiveCombatRefuge(body),
                    "Completed Town23 refuge retained combat-action suppression.");

                int completedCalls = binding.NativeDelegatedActionCount;
                probeActor.Place(completeAt + 1);
                guards.enabled = originalEnabled;
                motor.enabled = true;
                motor.TakeActionHandler();
                guards.enabled = false;
                Require(binding.NativeDelegatedActionCount > completedCalls &&
                    liveRoster.Guards.Find(guard => guard.Id == probeGuardId).Duty == GuardDuty.Posted,
                    "Completed refuge was immediately re-enlisted instead of returning to native action.");

                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CITYWATCH_WALL_RETREAT_PASS: guard=" + probeGuardId +
                    "; encounter-roll=" + roll + "; target-load-id=" + targetLoadId.ToString(CultureInfo.InvariantCulture) +
                    "; town23=" + refugeBuildingKey + "; route-persisted=true; approach-and-return=physical-roster-placement; " +
                    "shelter-seconds>=3600; completed=posted; live-save-writes=none; world-clock-changed=false.");
            }
            finally
            {
                guards.enabled = false;
                if (!probeActor && probeGuardId != null)
                    actorMap.TryGetValue(probeGuardId, out probeActor);
                if (probeActor)
                {
                    probeActor.EndCombat();
                    probeActor.gameObject.SetActive(false);
                    GuardActor owned;
                    if (probeGuardId != null && actorMap.TryGetValue(probeGuardId, out owned) && owned == probeActor)
                        actorMap.Remove(probeGuardId);
                    UnityEngine.Object.DestroyImmediate(probeActor.gameObject);
                }
                if (targetObject) UnityEngine.Object.DestroyImmediate(targetObject);
                if (restoreRoster) RestoreRosterInPlace(liveRoster, rosterBackup);
                if (!ReferenceEquals(refugeGeometryField.GetValue(guards), originalRefugeGeometry))
                    refugeGeometryField.SetValue(guards, originalRefugeGeometry);
                RestoreDictionary((IDictionary)retryField.GetValue(guards), retryBefore);
                var cache = (IDictionary)refugeCacheField.GetValue(guards);
                var removeKeys = new List<object>();
                foreach (object key in cache.Keys) if (!refugeKeysBefore.Contains(key)) removeKeys.Add(key);
                foreach (object key in removeKeys) cache.Remove(key);
                guards.enabled = originalEnabled;
                UnityEngine.Random.state = randomState;
                DFRandom.Seed = dfRandomSeed;
                Physics.SyncTransforms();
            }
        }

        private static GameObject SpawnInertTarget(Vector3 position, ulong loadId)
        {
            GameObject body = null;
            try
            {
                var randomState = UnityEngine.Random.state;
                uint seed = DFRandom.Seed;
                try
                {
                    var actors = GameObjectHelper.CreateFoeGameObjects(position, MobileTypes.Thief, 1, MobileReactions.Passive);
                    Require(actors != null && actors.Length == 1 && actors[0],
                        "DFU did not create disposable native City Watch target.");
                    body = actors[0];
                    body.SetActive(false);
                    body.name = ProbeName;
                    body.hideFlags = HideFlags.HideAndDontSave;
                    var native = body.GetComponent<DaggerfallEnemy>();
                    Require(native, "Disposable City Watch target has no DaggerfallEnemy.");
                    native.LoadID = 0;
                    foreach (var serializer in body.GetComponentsInChildren<SerializableEnemy>(true))
                        UnityEngine.Object.DestroyImmediate(serializer);
                    native.LoadID = loadId;
                    var motor = body.GetComponent<EnemyMotor>();
                    var senses = body.GetComponent<EnemySenses>();
                    var attack = body.GetComponent<EnemyAttack>();
                    if (motor) motor.enabled = false;
                    if (senses) senses.enabled = false;
                    if (attack) attack.enabled = false;
                    body.SetActive(true);
                    return body;
                }
                finally
                {
                    UnityEngine.Random.state = randomState;
                    DFRandom.Seed = seed;
                }
            }
            catch
            {
                if (body) UnityEngine.Object.DestroyImmediate(body);
                throw;
            }
        }

        private static ulong FindEligibleTargetLoadId(uint worldSeed, string guardId, out int selectedRoll)
        {
            // Prefix deliberately sits outside ordinary save-game IDs. Serializer component
            // is removed from the fixture, so this ID exists only for deterministic hashing.
            const ulong prefix = 0xD1F0000000000000UL;
            for (ulong i = 1; i <= 10000; i++)
            {
                ulong candidate = prefix | i;
                int roll;
                if (NativeNpcCombatRoleDecision.TryGetEncounterRoll(worldSeed, 0, guardId,
                    candidate, null, out roll) && roll < NpcRolePolicy.CityGuardRetreatChancePercent)
                {
                    selectedRoll = roll;
                    return candidate;
                }
            }
            throw new InvalidOperationException("Could not find stable City Watch target LoadID for roll 0 or 1.");
        }

        private static double RouteLength(IList<GuardPoint3> route)
        {
            double total = 0;
            for (int i = 1; i < route.Count; i++)
            {
                double dx = route[i].X - route[i - 1].X;
                double dy = route[i].Y - route[i - 1].Y;
                double dz = route[i].Z - route[i - 1].Z;
                total += Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            return total;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, InstancePrivate);
            if (field == null) throw new InvalidOperationException("Required probe field is missing: " + name + ".");
            return field;
        }

        private static Dictionary<object, object> CopyDictionary(IDictionary source)
        {
            var copy = new Dictionary<object, object>();
            foreach (DictionaryEntry entry in source) copy.Add(entry.Key, entry.Value);
            return copy;
        }

        private static void RestoreDictionary(IDictionary target, Dictionary<object, object> source)
        {
            target.Clear();
            foreach (var entry in source) target.Add(entry.Key, entry.Value);
        }

        private static void RestoreRosterInPlace(GuardRoster target, GuardRoster snapshot)
        {
            foreach (FieldInfo field in typeof(GuardRoster).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (!field.IsInitOnly) field.SetValue(target, field.GetValue(snapshot));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
