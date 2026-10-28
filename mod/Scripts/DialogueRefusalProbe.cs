using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in native regression. Uses an existing managed adult in the loaded
    // disposable town save and submits a real direct threat through chat.
    internal static class DialogueRefusalProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Threat = "I will kill your family.";
        private const string SaveTitle = "People of the Iliac Bay - family threat refusal";
        private const string OwnedCharacter = "Hakon Ironhand";
        private const string OwnedBaselineSave = "People - Gothway Garden";

        private sealed class KeeperFixture
        {
            public Transform DoorOwner;
            public StaticDoor EntryDoor;
            public int SettlementId, BuildingKey, KeeperHash;
            public string KeeperId;
        }

        public static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string requestedCharacter = ArgumentValue(arguments, "-peopleoftheiliacbay-fixture-character");
            string baselineSave = ArgumentValue(arguments, "-peopleoftheiliacbay-fixture-save");
            var game = GameManager.Instance;
            if (!host || !streets || !game || !string.Equals(character, OwnedCharacter, StringComparison.Ordinal) ||
                !string.Equals(game.PlayerEntity.Name, OwnedCharacter, StringComparison.Ordinal) ||
                !string.Equals(requestedCharacter, OwnedCharacter, StringComparison.Ordinal) ||
                !string.Equals(baselineSave, OwnedBaselineSave, StringComparison.Ordinal))
                throw new InvalidOperationException("Family-threat refusal probe requires the exact owned character and explicit baseline: " +
                    OwnedCharacter + " / " + OwnedBaselineSave + ".");
            game.SaveLoadManager.EnumerateSaves();
            if (game.SaveLoadManager.FindSaveFolderByNames(OwnedCharacter, OwnedBaselineSave) < 0)
                throw new InvalidOperationException("Family-threat refusal baseline save is missing before mutation: " + OwnedBaselineSave + ".");

            Exception probeFailure = null, cleanupFailure = null;
            var probe = new NestedEnumeratorDriver(RunCore(host, streets, character));
            bool completed = false;
            try
            {
                object yielded;
                while (probe.TryMoveNext(out yielded)) yield return yielded;
                probeFailure = probe.Failure;
                completed = true;
            }
            finally
            {
                probe.Dispose();
                try { CancelOwnedTransport(host); }
                catch (Exception error) { AppendFailure(ref cleanupFailure, error); }
                try { if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD(); }
                catch (Exception error) { AppendFailure(ref cleanupFailure, error); }
                if (!completed)
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_BASELINE_RESTORE_SKIPPED_EXTERNAL_CANCEL: " +
                        "transport cancelled and Talk closed synchronously; Unity coroutine cancellation cannot yield a disk reload.");
            }
            if (!completed) yield break;

            bool cleanupFlowFinished = false;
            NestedEnumeratorDriver restore = null;
            try
            {
                var close = new NestedEnumeratorDriver(ConversationMoodProbe.CloseTalk(host));
                try
                {
                    object yielded;
                    while (close.TryMoveNext(out yielded)) yield return yielded;
                }
                finally { close.Dispose(); }
                AppendFailure(ref cleanupFailure, close.Failure);

                restore = new NestedEnumeratorDriver(ConversationMoodProbe.LoadSave(host, streets,
                    OwnedCharacter, OwnedBaselineSave, true));
                try
                {
                    object yielded;
                    while (restore.TryMoveNext(out yielded)) yield return yielded;
                }
                finally { restore.Dispose(); }
                AppendFailure(ref cleanupFailure, restore.Failure);
                cleanupFlowFinished = true;
            }
            finally
            {
                if (!cleanupFlowFinished)
                {
                    try { CancelOwnedTransport(host); }
                    catch (Exception error) { AppendFailure(ref cleanupFailure, error); }
                    try { if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD(); }
                    catch (Exception error) { AppendFailure(ref cleanupFailure, error); }
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_BASELINE_RESTORE_SKIPPED_EXTERNAL_CANCEL: " +
                        "transport cancelled and Talk closed synchronously while asynchronous baseline reload was interrupted.");
                }
            }
            if (restore.Failure == null)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_BASELINE_RESTORED: character=" + OwnedCharacter +
                    "; save=" + OwnedBaselineSave + ".");
            else
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_BASELINE_RESTORE_FAILED: character=" + OwnedCharacter +
                    "; save=" + OwnedBaselineSave + "; error=" + restore.Failure);

            if (probeFailure != null && cleanupFailure != null)
                throw new AggregateException("Family-threat probe failed and baseline cleanup also failed.", probeFailure, cleanupFailure);
            if (probeFailure != null) throw probeFailure;
            if (cleanupFailure != null) throw cleanupFailure;
        }

        private static IEnumerator RunCore(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            var game = GameManager.Instance;
            var guards = host ? host.GetComponent<GuardPopulation>() : null;
            if (!host || !streets || !game || !NativeProbeText.IsTestCharacter(character))
                throw new InvalidOperationException("Family-threat refusal probe requires the owned disposable town fixture.");
            float readyDeadline = Time.realtimeSinceStartup + 60f;
            while ((!streets.Ready || !guards || !guards.OwnsSettlement || !guards.HasAvailableDialogueGuards()) &&
                Time.realtimeSinceStartup < readyDeadline) yield return null;
            if (!streets.Ready || !guards || !guards.OwnsSettlement || !guards.HasAvailableDialogueGuards())
                throw new InvalidOperationException("Family-threat probe requires a ready settlement garrison for the natural outdoor report.");
            if (game.PlayerEnterExit.IsPlayerInsideBuilding)
                throw new InvalidOperationException("Family-threat probe requires the loaded exterior fixture.");

            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            double originalWorldSeconds = clock.ToSeconds();
            double morningTarget = Math.Floor(originalWorldSeconds / 86400d) * 86400d + 10d * 3600d;
            if (originalWorldSeconds < morningTarget)
            {
                clock.RaiseTime((float)(morningTarget - originalWorldSeconds));
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_PROBE_CLOCK: originalSeconds=" + originalWorldSeconds.ToString("F0") +
                    "; advancedSeconds=" + clock.ToSeconds().ToString("F0") + "; target=10:00 local day time.");
            }

            var store = GetStore(host);
            ResidentActor unrelated = FindEligibleActor(store, streets);
            float actorDeadline = Time.realtimeSinceStartup + 30f;
            while (!unrelated && Time.realtimeSinceStartup < actorDeadline)
            {
                yield return new WaitForSecondsRealtime(0.2f);
                unrelated = FindEligibleActor(store, streets);
            }
            if (!unrelated) throw new InvalidOperationException("No unrelated adult resident is available for the entry control: " +
                ActorEligibilitySummary(store, streets));
            game.TalkManager.TalkToMobileNPC(unrelated.Person);
            float controlDeadline = Time.realtimeSinceStartup + 20f;
            while (!(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow) && Time.realtimeSinceStartup < controlDeadline)
                yield return null;
            if (!(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow))
                throw new InvalidOperationException("Ordinary unrelated resident could not open native Talk before the refusal test.");
            DaggerfallUI.Instance.PopToHUD();
            yield return null;

            ResidentActor actor = FindEligibleActor(store, streets, unrelated.Resident.Id);
            actorDeadline = Time.realtimeSinceStartup + 30f;
            while (!actor && Time.realtimeSinceStartup < actorDeadline)
            {
                yield return new WaitForSecondsRealtime(0.2f);
                actor = FindEligibleActor(store, streets, unrelated.Resident.Id);
            }
            if (!actor) throw new InvalidOperationException("No eligible existing adult managed resident is available for the threat fixture: " +
                ActorEligibilitySummary(store, streets));
            string id = actor.Resident.Id;
            string name = actor.Person.NameNPC;

            game.TalkManager.TalkToMobileNPC(actor.Person);
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow) && Time.realtimeSinceStartup < deadline)
                yield return null;
            var talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            if (talk == null) throw new InvalidOperationException("Native conversation entry did not open for the selected resident.");
            deadline = Time.realtimeSinceStartup + 15f;
            while ((!talk.IsSetup || !IsConversationSessionOpen(host) || IsConversationBusy(host)) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            if (!talk.IsSetup || !IsConversationSessionOpen(host) || IsConversationBusy(host))
                throw new InvalidOperationException("Native Talk session did not initialize before threat submission.");

            float indexDeadline=Time.realtimeSinceStartup+5f;
            while(!store.IsConversationRecallIndexReady(id)&&Time.realtimeSinceStartup<indexDeadline)
            {
                if(!(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow activeTalk) || activeTalk!=talk ||
                    !IsConversationSessionOpen(host) || GetConversationPersonId(host)!=id)
                    throw new InvalidOperationException("Managed speaker changed before active-dialogue recall index readiness.");
                yield return null;
            }
            if(!store.IsConversationRecallIndexReady(id))
                throw new InvalidOperationException("Actual managed speaker's dialogue recall index did not warm within 5 seconds.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTIVE_DIALOGUE_INDEX_PASS: person="+id+
                "; activeWindow=true; runtimeWarmup=true; ready=true; manualAdvance=false.");

            var submit = typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText", Private);
            if (submit == null) throw new InvalidOperationException("Production free-text submission entry point was not found.");
            yield return VerifyOrdinaryReactions(host,game,actor,talk,submit,value=>talk=value);
            submit.Invoke(host, new object[] { talk, Threat });
            NpcObservedSpeechReceipt expectedSpeech = CaptureSubmittedSpeech(host, id);
            deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                store = GetStore(host);
                if (!IsConversationBusy(host) && !(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow) &&
                    store.TryGetActualConversationDisposition(id, out int disposition) && disposition <= -100)
                    break;
                yield return null;
            }
            if (IsConversationBusy(host) || DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow ||
                !store.TryGetActualConversationDisposition(id, out int actual) || actual > -100)
                throw new InvalidOperationException("Direct family threat did not terminate the real conversation at native disposition -100.");

            NpcConversationBoundaryRecord mobileClassification = AssertThreatMemory(store, id, expectedSpeech, null);
            NpcConversationBoundaryRecord mobileDiskExpectation = CanonicalizeForNativeDisk(mobileClassification, expectedSpeech);
            TryReopenAndAssertRefusal(game, actor, name);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_MOBILE_STAGE: before-save id=" + id + ".");
            yield return SaveAndReload(game, host, streets, character);

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_MOBILE_STAGE: after-load id=" + id + ".");
            store = GetStore(host);
            AssertThreatMemory(store, id, expectedSpeech, mobileDiskExpectation);
            if (!store.TryGetActualConversationDisposition(id, out actual) || actual > -100)
                throw new InvalidOperationException("Terminal refusal disposition did not survive native save/reload.");
            actor = FindActor(id);
            actorDeadline = Time.realtimeSinceStartup + 30f;
            while (!actor && Time.realtimeSinceStartup < actorDeadline)
            {
                yield return new WaitForSecondsRealtime(0.2f);
                actor = FindActor(id);
            }
            if (!actor || !actor.Person) throw new InvalidOperationException("Threatened resident was not present after native reload: " +
                ActorEligibilitySummary(store, streets));
            TryReopenAndAssertRefusal(game, actor, actor.Person.NameNPC);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_MOBILE_REFUSAL_PASS: real direct threat; forced close; exact saved exchange; native save/reload; mobile chat entry refused for " + id + ".");

            yield return RunStaticKeeperRefusal(game, host, streets, character);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_REFUSAL_PASS: mobile and static native chat entry refused after real threat and save/reload.");
        }

        private static IEnumerator RunStaticKeeperRefusal(GameManager game, PeopleOfTheIliacBayMod host,
            StreetPopulation streets, string character)
        {
            float deadline = Time.realtimeSinceStartup + 45f;
            while ((game.PlayerEnterExit.IsPlayerInside || game.StreamingWorld.IsRepositioningPlayer ||
                !game.StreamingWorld.IsReady) && Time.realtimeSinceStartup < deadline) yield return null;
            if (game.PlayerEnterExit.IsPlayerInside || game.StreamingWorld.IsRepositioningPlayer ||
                !game.StreamingWorld.IsReady)
                throw new InvalidOperationException("Static refusal phase requires a ready exterior after the mobile save/reload.");

            WorldStateStore store = GetStore(host);
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            int settlement = game.PlayerGPS.CurrentLocation.MapTableData.MapId;
            KeeperFixture fixture = SelectOwnedTavern(directory, store, settlement);
            if (fixture == null)
                throw new InvalidOperationException("No existing tavern door has a living registered native keeper in the loaded settlement.");

            TownSmokeHarness.EnterInterior(fixture.DoorOwner, fixture.EntryDoor);
            deadline = Time.realtimeSinceStartup + 45f;
            while ((!game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerEnterExit.Interior ||
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey != fixture.BuildingKey) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            if (!game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerEnterExit.Interior ||
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey != fixture.BuildingKey)
                throw new InvalidOperationException("Native entry into the existing registered keeper's tavern timed out.");

            StaticNPC keeper = FindFixtureKeeper(game, store, fixture);
            deadline = Time.realtimeSinceStartup + 30f;
            while (!keeper && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                keeper = FindFixtureKeeper(game, store, fixture);
            }
            if (!keeper) throw new InvalidOperationException("Registered native keeper did not materialize in the selected tavern interior.");

            OpenKeeperConversation(game, keeper);
            DaggerfallTalkWindow talk = null;
            string staticId = NativeStaticId(keeper);
            deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline)
            {
                talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                if (talk != null && talk.IsSetup && IsConversationSessionOpen(host) && !IsConversationBusy(host) &&
                    GetConversationPersonId(host) == staticId) break;
                yield return null;
            }
            if (talk == null || !talk.IsSetup || !IsConversationSessionOpen(host) || IsConversationBusy(host) ||
                GetConversationPersonId(host) != staticId)
                throw new InvalidOperationException("Native static keeper conversation did not initialize for the exact saved keeper identity.");

            var submit = typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText", Private);
            if (submit == null) throw new InvalidOperationException("Production free-text submission entry point was not found for the static keeper.");
            submit.Invoke(host, new object[] { talk, Threat });
            NpcObservedSpeechReceipt expectedSpeech = CaptureSubmittedSpeech(host, staticId);
            deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                store = GetStore(host);
                if (!IsConversationBusy(host) && !(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow) &&
                    store.TryGetActualConversationDisposition(staticId, out int disposition) && disposition <= -100)
                    break;
                yield return null;
            }
            if (IsConversationBusy(host) || DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow ||
                !store.TryGetActualConversationDisposition(staticId, out int actual) || actual > -100)
                throw new InvalidOperationException("Direct threat did not terminate static keeper Talk at native disposition -100.");
            NpcConversationBoundaryRecord staticClassification = AssertThreatMemory(store, staticId, expectedSpeech, null);
            NpcConversationBoundaryRecord staticDiskExpectation = CanonicalizeForNativeDisk(staticClassification, expectedSpeech);
            TryReopenStaticAndAssertRefusal(game, keeper);

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_STATIC_STAGE: before-save id=" + staticId + ".");
            yield return SaveAndReload(game, host, streets, character);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_STATIC_STAGE: after-load id=" + staticId + ".");
            store = GetStore(host);
            AssertThreatMemory(store, staticId, expectedSpeech, staticDiskExpectation);
            if (!store.TryGetActualConversationDisposition(staticId, out actual) || actual > -100)
                throw new InvalidOperationException("Static keeper refusal disposition did not survive native save/reload.");
            if (!game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerEnterExit.Interior ||
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey != fixture.BuildingKey)
                throw new InvalidOperationException("Native save/reload did not restore the selected keeper's interior for same-identity verification.");
            keeper = FindFixtureKeeper(game, store, fixture);
            deadline = Time.realtimeSinceStartup + 30f;
            while (!keeper && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                keeper = FindFixtureKeeper(game, store, fixture);
            }
            if (!keeper) throw new InvalidOperationException("Same registered static keeper was not present after native reload.");
            TryReopenStaticAndAssertRefusal(game, keeper);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT_STATIC_REFUSAL_PASS: real registered keeper; exact saved exchange; native save/reload; static Talk entry refused for " + staticId + ".");
        }

        private static KeeperFixture SelectOwnedTavern(BuildingDirectory directory, WorldStateStore store, int settlement)
        {
            if (!directory || store == null) return null;
            var people = store.NativePeopleFor(settlement);
            foreach (var building in directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern))
            foreach (var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            foreach (var door in doors.Doors)
            {
                if (door.doorType != DoorTypes.Building || door.buildingKey != building.buildingKey) continue;
                var keeper = people.Find(person => person.IsAlive && person.WorkKey == building.buildingKey);
                if (keeper == null) continue;
                return new KeeperFixture { DoorOwner = doors.transform, EntryDoor = door,
                    SettlementId = settlement, BuildingKey = building.buildingKey,
                    KeeperId = keeper.Id, KeeperHash = keeper.NativeHash };
            }
            return null;
        }

        private static StaticNPC FindFixtureKeeper(GameManager game, WorldStateStore store, KeeperFixture fixture)
        {
            if (!game.PlayerEnterExit.Interior || fixture == null) return null;
            var record = store.NativePeopleFor(fixture.SettlementId).Find(person => person.Id == fixture.KeeperId &&
                person.IsAlive && person.WorkKey == fixture.BuildingKey && person.NativeHash == fixture.KeeperHash);
            if (record == null) return null;
            foreach (var candidate in game.PlayerEnterExit.Interior.GetComponentsInChildren<StaticNPC>(true))
                if (candidate && candidate.gameObject.activeInHierarchy && candidate.Data.context == StaticNPC.Context.Building &&
                    candidate.Data.mapID == fixture.SettlementId && candidate.Data.buildingKey == fixture.BuildingKey &&
                    candidate.Data.hash == fixture.KeeperHash) return candidate;
            return null;
        }

        private static void OpenKeeperConversation(GameManager game, StaticNPC keeper)
        {
            game.TalkManager.TalkToStaticNPC(keeper);
            var click = typeof(PlayerActivate).GetMethod("StaticNPCClick", Private);
            if (click == null) throw new InvalidOperationException("Native static NPC conversation handler is unavailable.");
            click.Invoke(game.PlayerActivate, new object[] { keeper });
        }

        private static void TryReopenStaticAndAssertRefusal(GameManager game, StaticNPC keeper)
        {
            if (DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow) DaggerfallUI.Instance.PopToHUD();
            game.TalkManager.TalkToStaticNPC(keeper);
            if (DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)
            {
                DaggerfallUI.Instance.PopToHUD();
                throw new InvalidOperationException("Terminally hostile static keeper reopened a neutral native Talk window.");
            }
            // Native entry rejection is the behavior under test. HUD text is
            // queued for a later frame and may use the player's known name.
        }

        private static string NativeStaticId(StaticNPC person)
        {
            var data = person.Data;
            return "npc:" + data.mapID + ":" + data.buildingKey + ":" + data.hash;
        }

        private static string GetConversationPersonId(PeopleOfTheIliacBayMod host)
        { return (string)typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId", Private).GetValue(host); }

        private static WorldStateStore GetStore(PeopleOfTheIliacBayMod host)
        { return typeof(PeopleOfTheIliacBayMod).GetField("store", Private).GetValue(host) as WorldStateStore; }

        private static bool IsConversationBusy(PeopleOfTheIliacBayMod host)
        { return (bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy", Private).GetValue(host); }

        private static bool IsConversationSessionOpen(PeopleOfTheIliacBayMod host)
        { return (bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", Private).GetValue(host); }

        private static ResidentActor FindEligibleActor(WorldStateStore store, StreetPopulation streets, string excludedId = null)
        {
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (!actor || !actor.Person || actor.Resident == null || !actor.Resident.IsAlive ||
                    actor.Resident.AgeGroup != ResidentAgeGroup.Adult || actor.IsConversing || actor.IsSleeping) continue;
                if (actor.Resident.Id == excludedId || streets.IsAlarmed(actor.Resident.Id) ||
                    store.HoldsPartnerGrudge(actor.Resident, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())) continue;
                if (store.TryGetActualConversationDisposition(actor.Resident.Id, out int disposition) && disposition <= -100) continue;
                if (store.GetDialogueComplaint(actor.Resident.Id) != null) continue;
                return actor;
            }
            return null;
        }

        private static string ActorEligibilitySummary(WorldStateStore store, StreetPopulation streets)
        {
            int loaded = 0, managedAdults = 0, freeAwakeAdults = 0, nonHostile = 0, nonAlarmed = 0,
                noGrudge = 0, noComplaint = 0, eligible = 0;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (!actor) continue;
                loaded++;
                if (!actor.Person || actor.Resident == null || !actor.Resident.IsAlive ||
                    actor.Resident.AgeGroup != ResidentAgeGroup.Adult) continue;
                managedAdults++;
                if (actor.IsConversing || actor.IsSleeping) continue;
                freeAwakeAdults++;
                if (store.TryGetActualConversationDisposition(actor.Resident.Id, out int disposition) && disposition <= -100) continue;
                nonHostile++;
                if (streets.IsAlarmed(actor.Resident.Id)) continue;
                nonAlarmed++;
                if (store.HoldsPartnerGrudge(actor.Resident, now)) continue;
                noGrudge++;
                if (store.GetDialogueComplaint(actor.Resident.Id) != null) continue;
                noComplaint++;
                eligible++;
            }
            return "actors=" + loaded + "; adultManaged=" + managedAdults + "; freeAwake=" + freeAwakeAdults +
                "; notTerminal=" + nonHostile + "; notAlarmed=" + nonAlarmed + "; noGrudge=" + noGrudge +
                "; noComplaint=" + noComplaint + "; eligible=" + eligible;
        }

        private static ResidentActor FindActor(string id)
        {
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if (actor && actor.Person && actor.Resident != null && actor.Resident.Id == id) return actor;
            return null;
        }

        private static NpcObservedSpeechReceipt CaptureSubmittedSpeech(PeopleOfTheIliacBayMod host, string id)
        {
            var field = typeof(PeopleOfTheIliacBayMod).GetField("conversationObservedSpeech", Private);
            var value = field == null ? null : field.GetValue(host) as NpcObservedSpeechReceipt;
            if (value == null) throw new InvalidOperationException("Submitted native speech did not retain its exact receipt before asynchronous route completion.");
            var copy = NpcObservedSpeechReceipt.CopyValidated(value);
            if (copy.Turn.ObserverId != id || copy.Turn.SpeakerId != NpcDirectedRelationship.PlayerTargetId ||
                copy.Turn.Text != Threat || copy.Evidence.ReceiptId != copy.Turn.ReceiptId)
                throw new InvalidOperationException("Captured native speech receipt does not match the exact probe turn and speaker.");
            return copy;
        }

        private static IEnumerator VerifyOrdinaryReactions(PeopleOfTheIliacBayMod host,GameManager game,
            ResidentActor actor,DaggerfallTalkWindow talk,MethodInfo submit,Action<DaggerfallTalkWindow> completed)
        {
            string id=actor.Resident.Id;
            // Use the existing no-audio fixture seam: toggling npcVoiceEnabled
            // does not detach an already-created prepared-speech bridge.
            var voice=typeof(PeopleOfTheIliacBayMod).GetField("breezeTtsTrial",Private);
            var oldVoice=voice.GetValue(host);
            voice.SetValue(host,null);
            try
            {
                string[] turns={"Where is an armor shop nearby?",
                    "That was not helpful. I asked for a nearby armor shop, not every trader in town.",
                    "You are a fool.","But where is the armor shop?",
                    "I am sorry I insulted you. That was cruel and you did not deserve it."};
                long insultOrdinal=0;
                for(int index=0;index<turns.Length;index++)
                {
                    if(index==3 || index==4)
                        yield return ConversationMoodProbe.OpenTalk(host,game,actor,value=>talk=value);
                    submit.Invoke(host,new object[]{talk,turns[index]});
                    float deadline=Time.realtimeSinceStartup+90;
                    while(IsConversationBusy(host) && Time.realtimeSinceStartup<deadline)yield return null;
                    var store=GetStore(host);double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                    var entry=store.ConversationTranscript(id,now,256).FindLast(row=>row.PlayerText==turns[index]);
                    var boundary=store.ReadNpcConversationBoundary(id,now);
                    bool open=DaggerfallUI.UIManager.TopWindow==talk;
                    if(IsConversationBusy(host) || entry==null || entry.Source=="unavailable" ||
                        string.IsNullOrWhiteSpace(entry.NpcText) || store.GetDialogueComplaint(id)!=null)
                        throw new InvalidOperationException("Everyday dialogue failed: busy="+IsConversationBusy(host)+
                            "; source="+entry?.Source+"; report="+(store.GetDialogueComplaint(id)!=null)+"; text="+turns[index]);
                    if(index<2 && (!open || boundary!=null || entry.NpcText.IndexOf("haven't agreed",StringComparison.OrdinalIgnoreCase)>=0))
                        throw new InvalidOperationException("A harmless direction question or criticism became an offense/action refusal.");
                    if(index==2)
                    {
                        if(open || boundary==null || boundary.Action!=ConversationSpeechBoundaryAction.EndConversation ||
                            boundary.ReportStatus!=NpcConversationBoundaryReportStatus.None || !store.CanTalkToNpcAfterBoundary(id,now))
                            throw new InvalidOperationException("Insult must close Talk without police and permit a later approach.");
                        insultOrdinal=boundary.Ordinal;
                        if(store.CaptureConversationMood(id,now).Value>=0 ||
                            store.ReadNpcRelationship(id,NpcDirectedRelationship.PlayerTargetId,now).ProjectedResentment<=0)
                            throw new InvalidOperationException("Insult did not retain anger and resentment.");
                    }
                    if(index==3 && (open || boundary==null || boundary.Ordinal!=insultOrdinal ||
                        boundary.ReportStatus!=NpcConversationBoundaryReportStatus.None))
                        throw new InvalidOperationException("Ordinary contact after refusal invented another offense or report.");
                    if(index==4 && (!open || store.HasUnrepairedNpcConversationBoundary(id,now) ||
                        store.ReadNpcRelationship(id,NpcDirectedRelationship.PlayerTargetId,now).ProjectedResentment<=0))
                        throw new InvalidOperationException("Sincere apology must reopen communication without erasing resentment.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_REACTION_TURN_PASS: step="+index+"; player="+turns[index]+"; npc="+entry.NpcText);
                }
                completed(talk);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_REACTIONS_PASS: ordinaryQuestion=true; criticism=true; insultCloses=true; reopenRefusal=true; apologyAllowsTalk=true; resentmentRetained=true; noUnfoundedReport=true; fullSubmitFreeText=true");
            }
            finally{voice.SetValue(host,oldVoice);}
        }

        private static NpcConversationBoundaryRecord AssertThreatMemory(WorldStateStore store, string id,
            NpcObservedSpeechReceipt expectedSpeech, NpcConversationBoundaryRecord expectedBeforeSave)
        {
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var boundary=store.ReadNpcConversationBoundary(id,now);
            var matches=store.ConversationTranscript(id,now,256).FindAll(value=>value.Source=="action" && value.PlayerText==Threat);
            var entry=matches.Count==1?matches[0]:null;
            if(boundary==null || boundary.SpeechKind!=NpcExperienceSpeechActKind.DirectThreat ||
                boundary.Action!=ConversationSpeechBoundaryAction.EndAndReport || expectedSpeech==null ||
                boundary.PersonId!=id || boundary.PersonId!=expectedSpeech.Turn.ObserverId ||
                boundary.ReceiptId!=expectedSpeech.Turn.ReceiptId ||
                boundary.ObservedAt!=(expectedBeforeSave==null?expectedSpeech.Turn.ObservedGameTime:expectedBeforeSave.ObservedAt) ||
                entry==null || string.IsNullOrWhiteSpace(entry.NpcText) ||
                entry.NpcText.IndexOf("Stay away from me",StringComparison.OrdinalIgnoreCase)<0)
                throw new InvalidOperationException("Saved threat boundary or exact transcript is missing.");
            if(expectedBeforeSave!=null && (boundary.Ordinal!=expectedBeforeSave.Ordinal ||
                boundary.SpeechKind!=expectedBeforeSave.SpeechKind || boundary.ReceiptId!=expectedBeforeSave.ReceiptId ||
                boundary.RefuseUntil!=expectedBeforeSave.RefuseUntil || boundary.Action!=expectedBeforeSave.Action))
                throw new InvalidOperationException("Native save/load changed the exact threat boundary.");
            if(store.CaptureConversationMood(id,now).Value>=0 ||
                store.ReadNpcRelationship(id,NpcDirectedRelationship.PlayerTargetId,now).ProjectedResentment<=0)
                throw new InvalidOperationException("Threat lost its negative mood or resentment.");
            if(entry.NpcText.IndexOf("The guards have been alerted",StringComparison.OrdinalIgnoreCase)>=0)
                throw new InvalidOperationException("Outdoor threat response claimed a report before the witness reached a guard.");
            if(store.GetDialogueComplaint(id)!=null &&
                entry.NpcText.IndexOf("I am going to find a guard",StringComparison.OrdinalIgnoreCase)<0)
                throw new InvalidOperationException("Outdoor threat response did not describe its real report route.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SPEECH_BOUNDARY_NATIVE_PASS: kind=DirectThreat; action=EndAndReport; exactReceipt=true; exactTranscript=true; negativeMood=true; resentment=true; person="+id);
            return boundary;
        }

        private static NpcConversationBoundaryRecord CanonicalizeForNativeDisk(
            NpcConversationBoundaryRecord boundary,NpcObservedSpeechReceipt speech)
        {
            if(boundary==null || speech==null || boundary.PersonId!=speech.Turn.ObserverId ||
                boundary.ReceiptId!=speech.Turn.ReceiptId || boundary.ObservedAt!=speech.Turn.ObservedGameTime)
                throw new InvalidOperationException("Boundary is not bound to the captured native speech.");
            string json=SaveLoadManager.Serialize(typeof(NpcConversationBoundaryRecord),boundary,false);
            var canonical=SaveLoadManager.Deserialize(typeof(NpcConversationBoundaryRecord),json,false) as NpcConversationBoundaryRecord;
            if(canonical==null || canonical.PersonId!=boundary.PersonId || canonical.ReceiptId!=boundary.ReceiptId ||
                canonical.Ordinal!=boundary.Ordinal || canonical.SpeechKind!=boundary.SpeechKind)
                throw new InvalidOperationException("Native serializer changed the threat receipt identity.");
            return canonical;
        }

        private static void TryReopenAndAssertRefusal(GameManager game, ResidentActor actor, string name)
        {
            if (DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow) DaggerfallUI.Instance.PopToHUD();
            game.TalkManager.TalkToMobileNPC(actor.Person);
            if (DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)
            {
                DaggerfallUI.Instance.PopToHUD();
                throw new InvalidOperationException("Terminally hostile resident reopened a neutral native Talk window.");
            }
            // Do not confuse deferred HUD rendering with a reopened Talk window.
        }

        private static IEnumerator SaveAndReload(GameManager game, PeopleOfTheIliacBayMod host,
            StreetPopulation streets, string character)
        {
            bool saved = false, loaded = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSave += onSave;
            SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.Save(character, SaveTitle);
                float deadline = Time.realtimeSinceStartup + 60f;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Family-threat save timed out.");
                game.SaveLoadManager.EnumerateSaves();
                int slot = game.SaveLoadManager.FindSaveFolderByNames(character, SaveTitle);
                if (slot < 0) throw new InvalidOperationException("Family-threat save slot missing after save.");
                game.SaveLoadManager.Load(slot);
                DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 75f;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Family-threat native reload timed out.");
            }
            finally
            {
                SaveLoadManager.OnSave -= onSave;
                SaveLoadManager.OnLoad -= onLoad;
            }
            yield return null;
        }

        private static string ArgumentValue(string[] arguments, string name)
        {
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
        }

        private static void CancelOwnedTransport(PeopleOfTheIliacBayMod host)
        {
            ConversationMoodProbe.CancelTransport(host);
            var cancel = typeof(PeopleOfTheIliacBayMod).GetMethod("CancelConversationWork", Private);
            if (cancel != null) cancel.Invoke(host, null);
        }

        private static void AppendFailure(ref Exception current, Exception next)
        {
            if (next == null) return;
            current = current == null ? next : new AggregateException(current, next);
        }

        // Unity advances yielded child iterators outside the parent's MoveNext.
        // Drive the whole probe tree here so failures are captured and every
        // iterator's finally block is disposed before baseline restoration.
        private sealed class NestedEnumeratorDriver : IDisposable
        {
            private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
            private bool complete;
            public Exception Failure { get; private set; }

            public NestedEnumeratorDriver(IEnumerator root)
            {
                if (root == null) throw new ArgumentNullException("root");
                stack.Push(root);
            }

            public bool TryMoveNext(out object yielded)
            {
                yielded = null;
                if (complete || Failure != null) return false;
                while (stack.Count > 0)
                {
                    IEnumerator top = stack.Peek();
                    bool more;
                    object current = null;
                    try
                    {
                        more = top.MoveNext();
                        if (more) current = top.Current;
                    }
                    catch (Exception error) { SetFailure(error); return false; }
                    if (!more)
                    {
                        stack.Pop();
                        try { (top as IDisposable)?.Dispose(); }
                        catch (Exception error) { SetFailure(error); return false; }
                        continue;
                    }
                    IEnumerator nested = current as IEnumerator;
                    if (nested != null) { stack.Push(nested); continue; }
                    yielded = current;
                    return true;
                }
                complete = true;
                return false;
            }

            private void SetFailure(Exception error)
            {
                Failure = CombineFailure(Failure, error);
                DisposeStack();
            }

            private void DisposeStack()
            {
                while (stack.Count > 0)
                {
                    IEnumerator pending = stack.Pop();
                    try { (pending as IDisposable)?.Dispose(); }
                    catch (Exception error) { Failure = CombineFailure(Failure, error); }
                }
            }

            public void Dispose()
            {
                DisposeStack();
                complete = true;
            }
        }

        private static Exception CombineFailure(Exception current, Exception next)
        {
            if (next == null) return current;
            return current == null ? next : new AggregateException(current, next);
        }
    }
}
