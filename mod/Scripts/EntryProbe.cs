using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Controlled observers, but actual native building transitions and saved ledger.
    internal static class EntryProbe
    {
        internal static IEnumerator Run(PeopleOfTheIliacBayMod adapter, StreetPopulation streets, int key)
        {
            var game = GameManager.Instance;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            DaggerfallStaticDoors owner = null; StaticDoor entry = default(StaticDoor);
            foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach (var door in candidate.Doors)
                    if (door.doorType == DoorTypes.Building && door.buildingKey == key)
                    { owner = candidate; entry = door; }
            if (!owner || streets.HouseholdRoutineLocked(key) != true)
                throw new InvalidOperationException("Entry probe needs a locked residential door.");
            yield return new WaitForSecondsRealtime(0.6f);
            var hidden = new List<GameObject>();
            ResidentActor witness = null;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                hidden.Add(actor.gameObject);
                if (actor.Resident.IsAlive && streets.GetState(actor.Resident.Id, now).Activity != Activity.Sleep) witness = actor;
            }
            foreach (var guard in UnityEngine.Object.FindObjectsOfType<GuardActor>()) hidden.Add(guard.gameObject);
            if (!witness) throw new InvalidOperationException("Entry probe has no awake street witness.");
            var crime = game.PlayerEntity.CrimeCommitted;
            var guardOption = typeof(PeopleOfTheIliacBayMod).GetField("exteriorGuardsEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
            bool oldGuardOption = (bool)guardOption.GetValue(adapter);
            guardOption.SetValue(adapter, false);
            adapter.GetComponent<GuardPopulation>().Clear();
            int before = ((WorldState)adapter.GetSaveData()).EntryReports.Count;
            string witnessId = witness.Resident.Id;
            try
            {
                foreach (var obj in hidden) obj.SetActive(false);
                witness.gameObject.SetActive(true);
                witness.transform.position = game.PlayerMotor.transform.position + Vector3.right;
                Physics.SyncTransforms();
                if (!TheftWitnessSight.CanSee(witness.transform, game.PlayerMotor.transform.position, true, true))
                    throw new InvalidOperationException("Controlled entry witness has no sight line.");
                var args = new PlayerEnterExit.TransitionEventArgs(PlayerEnterExit.TransitionType.ToBuildingInterior, entry);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(StreetPopulation).GetMethod("CaptureEntry", flags).Invoke(streets, new object[] { args });
                if (typeof(StreetPopulation).GetField("pendingEntry", flags).GetValue(streets) == null)
                    throw new InvalidOperationException("Visible entry did not capture a pending witness.");
                if (((WorldState)adapter.GetSaveData()).EntryReports.Count != before)
                    throw new InvalidOperationException("Pre-transition observation committed too early.");
                typeof(StreetPopulation).GetMethod("CancelEntry", flags).Invoke(streets, new object[] { args });
                typeof(StreetPopulation).GetMethod("CommitEntry", flags).Invoke(streets, new object[] { args });
                if (((WorldState)adapter.GetSaveData()).EntryReports.Count != before)
                    throw new InvalidOperationException("Cancelled entry committed evidence.");
                TownSmokeHarness.EnterInterior(owner.transform, entry);
                yield return new WaitForSecondsRealtime(0.6f);
                var reports = ((WorldState)adapter.GetSaveData()).EntryReports;
                if (reports.Count != before + 1 || reports[before].BuildingKey != key ||
                    reports[before].CitizenWitnesses.Count != 1 || reports[before].CitizenWitnesses[0] != witnessId ||
                    reports[before].InformedGuards.Count != 0 || game.PlayerEntity.CrimeCommitted != crime)
                    throw new InvalidOperationException("Citizen entry evidence was lost or alerted uninformed guards.");
                game.PlayerEnterExit.TransitionExterior();
                yield return new WaitForSecondsRealtime(0.6f);
                foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                { hidden.Add(actor.gameObject); actor.gameObject.SetActive(false); }
                foreach (var guard in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                { hidden.Add(guard.gameObject); guard.gameObject.SetActive(false); }
                TownSmokeHarness.EnterInterior(owner.transform, entry);
                yield return new WaitForSecondsRealtime(0.6f);
                if (((WorldState)adapter.GetSaveData()).EntryReports.Count != before + 1 || game.PlayerEntity.CrimeCommitted != crime)
                    throw new InvalidOperationException("Unseen entry invented evidence or an accusation.");
                game.PlayerEnterExit.TransitionExterior();
                yield return new WaitForSecondsRealtime(0.6f);
                var guards = adapter.GetComponent<GuardPopulation>();
                var store = (WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(adapter);
                var population = (SettlementPopulation)typeof(PeopleOfTheIliacBayMod).GetField("current", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(adapter);
                guardOption.SetValue(adapter, true);
                guards.SetTown(directory, game.StreamingWorld.GetCurrentCityNavigation(), population, store);
                float deadline = Time.realtimeSinceStartup + 30;
                while ((guards.Roster == null || !guards.Roster.BuildingApproaches.Exists(a => a.BuildingKey == key)) &&
                    Time.realtimeSinceStartup < deadline) yield return null;
                if (guards.Roster == null || !guards.Roster.BuildingApproaches.Exists(a => a.BuildingKey == key))
                    throw new InvalidOperationException("Entry probe guard approaches did not finish surveying.");
                // Use surveyed outdoor geometry, then stay inside until the messenger arrives.
                ResidentActor courier = null;
                foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if (actor.Resident.IsAlive && streets.GetState(actor.Resident.Id, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).Activity != Activity.Sleep)
                        courier = actor;
                if (!courier) throw new InvalidOperationException("No civilian for messenger probe.");
                var approach = guards.Roster.BuildingApproaches.Find(a => a.BuildingKey == key);
                var path = approach.Routes[0].Points;
                var start = path[path.Count / 2];
                var geometry = new TownGeometry(directory, game.StreamingWorld.GetCurrentCityNavigation(), false);
                var startPoint = geometry.ScenePoint(new RoutePoint(start.X, start.Z));
                foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                { hidden.Add(actor.gameObject); actor.gameObject.SetActive(false); }
                foreach (var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                { hidden.Add(actor.gameObject); actor.gameObject.SetActive(false); }
                courier.gameObject.SetActive(true); courier.transform.position = startPoint + Vector3.up;
                game.PlayerMotor.transform.position = courier.GetComponent<Collider>().bounds.center;
                Physics.SyncTransforms();
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
                int messengerIndex = ((WorldState)adapter.GetSaveData()).EntryReports.Count;
                TownSmokeHarness.EnterInterior(owner.transform, entry);
                var messengerReport = ((WorldState)adapter.GetSaveData()).EntryReports[messengerIndex];
                if (messengerReport.Messenger == null || messengerReport.InformedGuards.Count != 0 ||
                    game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None)
                    throw new InvalidOperationException("Civilian entry did not start a delayed messenger route.");
                double midpoint = (messengerReport.Messenger.StartedAt + messengerReport.Messenger.ArrivesAt) / 2;
                var travelling = streets.GetState(messengerReport.Messenger.CitizenId, midpoint);
                var expectedPoint = messengerReport.Messenger.Position(midpoint);
                if (travelling.Activity != Activity.Travel || travelling.BuildingKey != -1 ||
                    Math.Abs(travelling.Position.X - expectedPoint.X) + Math.Abs(travelling.Position.Z - expectedPoint.Z) > 0.001)
                    throw new InvalidOperationException("Messenger did not override its ordinary schedule position.");
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(messengerReport.Messenger.ArrivesAt -
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() + 1));
                yield return new WaitForSecondsRealtime(0.6f);
                messengerReport = ((WorldState)adapter.GetSaveData()).EntryReports[messengerIndex];
                if (!game.PlayerEnterExit.IsPlayerInsideBuilding || !messengerReport.Messenger.Delivered ||
                    messengerReport.InformedGuards.Count != 1 || game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.Trespassing ||
                    !guards.Roster.Reports.Exists(r => r.Id == messengerReport.Id && r.GuardIds.Count == 1))
                    throw new InvalidOperationException("Off-screen messenger did not deliver and dispatch while player stayed indoors.");
                game.PlayerEntity.CrimeCommitted = crime;
                game.PlayerEnterExit.TransitionExterior();
                yield return new WaitForSecondsRealtime(0.6f);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTRY_MESSENGER_PASS: surveyed route, delayed off-screen notification and finite dispatch while indoors.");
                foreach (var priorCrime in new[] { PlayerEntity.Crimes.None, PlayerEntity.Crimes.Murder })
                {
                    DaggerfallUnity.Instance.WorldTime.Now.RaiseTime(2);
                    var posted = guards.Roster.Guards.Find(g => g.IsAlive && g.Duty == GuardDuty.Posted);
                    if (posted == null) throw new InvalidOperationException("Entry probe has no available guard post.");
                    game.PlayerMotor.transform.position = directory.transform.TransformPoint(
                        new Vector3((float)posted.Post.X, 1, (float)posted.Post.Z));
                    yield return new WaitForSecondsRealtime(0.6f);
                    GuardActor observer = null;
                    foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    { hidden.Add(actor.gameObject); actor.gameObject.SetActive(false); }
                    foreach (var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                    {
                        hidden.Add(actor.gameObject); actor.gameObject.SetActive(false);
                        if (guards.Roster.Guards.Exists(g => g.Id == actor.GuardId && g.IsAlive && g.Duty == GuardDuty.Posted)) observer = actor;
                    }
                    if (!observer) throw new InvalidOperationException("Entry probe has no posted guard witness.");
                    observer.gameObject.SetActive(true);
                    // Co-locate the controlled observation target with the guard's eye;
                    // side offsets can land behind a wall at a different guard post.
                    var observerBody = observer.GetComponent<Collider>();
                    game.PlayerMotor.transform.position = observerBody ? observerBody.bounds.center : observer.transform.position + Vector3.up;
                    Physics.SyncTransforms();
                    if (!TheftWitnessSight.CanSee(observer.transform, game.PlayerMotor.transform.position, true, true))
                        throw new InvalidOperationException("Controlled guard cannot see entry.");
                    string guardId = observer.GuardId;
                    int count = ((WorldState)adapter.GetSaveData()).EntryReports.Count;
                    game.PlayerEntity.CrimeCommitted = priorCrime;
                    TownSmokeHarness.EnterInterior(owner.transform, entry);
                    var saved = ((WorldState)adapter.GetSaveData()).EntryReports;
                    var expected = priorCrime == PlayerEntity.Crimes.None ? PlayerEntity.Crimes.Trespassing : priorCrime;
                    if (saved.Count != count + 1 || saved[count].InformedGuards.Count != 1 ||
                        saved[count].InformedGuards[0] != guardId || game.PlayerEntity.CrimeCommitted != expected)
                        throw new InvalidOperationException("Guard-witnessed entry lost evidence or replaced an existing crime.");
                    var dispatch = guards.Roster.Reports.Find(r => r.Id == saved[count].Id);
                    if (dispatch == null || dispatch.GuardIds.Count != 1 || dispatch.GuardIds[0] != guardId)
                        throw new InvalidOperationException("Entry did not dispatch exactly its informed guard.");
                    game.PlayerEntity.CrimeCommitted = crime;
                    game.PlayerEnterExit.TransitionExterior();
                    yield return new WaitForSecondsRealtime(0.6f);
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTRY_GUARD_PASS: direct guard witness dispatches only informed guard; Trespassing and prior Murder preserved.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTRY_PASS: native witnessed/unseen transitions, cancellation, civilian-only evidence without instant accusation.");
            }
            finally
            {
                game.PlayerEntity.CrimeCommitted = crime;
                guardOption.SetValue(adapter, oldGuardOption);
                foreach (var obj in hidden) if (obj) obj.SetActive(true);
            }
        }
    }
}
