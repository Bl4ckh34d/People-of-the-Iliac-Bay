using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Native integration check. Run on a disposable exterior town save with its garrison loaded.
    public static class DialogueHostilityProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private sealed class OwnedFixture
        {
            public Transform DoorOwner;
            public StaticDoor EntryDoor;
            public int SettlementId, BuildingKey, KeeperHash;
            public string KeeperId;
        }

        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            if (!adapter || game == null || !game.PlayerGPS.HasCurrentLocation)
                throw new InvalidOperationException("Dialogue hostility probe requires a ready town save.");
            var streets = adapter.GetComponent<StreetPopulation>();
            var guards = adapter.GetComponent<GuardPopulation>();
            var store = typeof(PeopleOfTheIliacBayMod).GetField("store", Private).GetValue(adapter) as WorldStateStore;
            float readyDeadline = Time.realtimeSinceStartup + 90f;
            while (!ProbeReady(game, adapter, streets, guards, store) && Time.realtimeSinceStartup < readyDeadline)
                yield return null;
            if (!ProbeReady(game, adapter, streets, guards, store))
                throw new InvalidOperationException("Dialogue hostility probe timed out waiting for restored settlement, garrison, and resident readiness.");
            if (!game.PlayerEnterExit.IsPlayerInsideBuilding)
                throw new InvalidOperationException("Dialogue hostility probe requires the pricing probe's inn interior for exact-owner checks.");
            var owned = CaptureOwnedFixture(game, store);
            yield return RunOwnedPlaceWarning(adapter, game, store, owned);
            game.PlayerEnterExit.TransitionExterior();
            float initialExitDeadline = Time.realtimeSinceStartup + 45f;
            while ((game.PlayerEnterExit.IsPlayerInside || game.StreamingWorld.IsRepositioningPlayer ||
                !game.StreamingWorld.IsReady) && Time.realtimeSinceStartup < initialExitDeadline)
                yield return null;
            if (game.PlayerEnterExit.IsPlayerInside || game.StreamingWorld.IsRepositioningPlayer ||
                !game.StreamingWorld.IsReady)
                throw new InvalidOperationException("Exterior transition before complaint probe timed out.");
            var beforeRoster = guards.Roster.Guards.Count;
            var oldReports = new HashSet<string>(StringComparer.Ordinal);
            foreach (var oldReport in guards.Roster.Reports) oldReports.Add(oldReport.Id);
            var originalCrime = game.PlayerEntity.CrimeCommitted;
            if (originalCrime != PlayerEntity.Crimes.None)
                throw new InvalidOperationException("Dialogue hostility probe requires no active player crime.");
            ResidentActor actor = null;
            float actorDeadline = Time.realtimeSinceStartup + 45f;
            while (!actor && Time.realtimeSinceStartup < actorDeadline)
            {
                // Exterior transition flags clear before the population rebuild finishes.
                yield return new WaitForSecondsRealtime(0.1f);
                if (game.PlayerEnterExit.IsPlayerInside || !streets.Ready) continue;
                foreach (var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if (candidate && candidate.Resident != null && candidate.Resident.IsAlive && !candidate.IsConversing &&
                        !candidate.IsSleeping && store.GetDialogueComplaint(candidate.Resident.Id) == null &&
                        store.ReadNpcConversationBoundary(candidate.Resident.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())==null)
                    { actor = candidate; break; }
            }
            if (!actor) throw new InvalidOperationException("No live managed street resident is available for the complaint fixture.");

            string personId = actor.Resident.Id;
            Vector3 start = actor.transform.position - Vector3.up;
            Vector3 last = start;
            bool moved = false, completed = false;
            try
            {
                if(streets.StartDialogueComplaint(actor,personId) || guards.TryReportLegacyDialogueComplaintAt(start) ||
                    game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None)
                    throw new InvalidOperationException("Receiptless dialogue dislike manufactured a crime report.");
                // Controlled native fixture: test physical reporting from an exact
                // issued threat receipt, independently of language-model quality.
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                const string threat="I will hurt you.";
                object source=new object();
                NpcObservedSpeechSession speechSession;NpcObservedSpeechTicket ticket;
                NpcSpeechActClassification classification;NpcConversationBoundaryRecord boundary;
                if(!store.TryBeginNpcObservedSpeechSession(personId,"native-hostility-probe:"+now,out speechSession) ||
                    !store.TryIssueNpcObservedSpeech(speechSession,1,threat,now,out ticket) ||
                    !NpcSpeechActClassification.TryAccept(NpcSpeechActRequest.Create(source,personId,"player",null,threat,""),
                        "DirectThreat",out classification) ||
                    !store.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,classification,out boundary) ||
                    store.TryAcceptNpcObservedSpeechAfterAppend(ticket,ticket.Receipt,NpcExperienceAppliedEffectsMask.None)==
                        NpcObservedSpeechAcceptanceResult.Rejected)
                    throw new InvalidOperationException("Could not issue the controlled native threat fixture.");
                if (!streets.StartDialogueComplaint(actor, personId,boundary.ReceiptId))
                    throw new InvalidOperationException("Could not start the native exterior complaint route.");
                var record = store.GetDialogueComplaint(personId);
                if (record == null) throw new InvalidOperationException("Exterior complaint was not saved.");
                game.TalkManager.TalkToMobileNPC(actor.Person);
                if (DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)
                    throw new InvalidOperationException("Reporting witness opened Talk while running to a guard.");
                if (game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None)
                    throw new InvalidOperationException("Exterior complaint reported before reaching a guard.");
                Vector3 savedCrime = actor.transform.parent.InverseTransformPoint(game.PlayerMotor.transform.position);
                if (Mathf.Abs((float)record.CrimeX - savedCrime.x) > 0.05f ||
                    Mathf.Abs((float)record.CrimeZ - savedCrime.z) > 0.05f)
                    throw new InvalidOperationException("Exterior complaint did not retain the player's original crime position.");

                float deadline = Time.realtimeSinceStartup + 120f;
                while (store.GetDialogueComplaint(personId) != null && Time.realtimeSinceStartup < deadline)
                {
                    yield return new WaitForSecondsRealtime(0.1f);
                    if (actor)
                    {
                        Vector3 current = actor.transform.position - Vector3.up;
                        if (Vector3.Distance(last, current) > 0.05f) moved = true;
                        last = current;
                    }
                }
                if (store.GetDialogueComplaint(personId) != null)
                    throw new InvalidOperationException("Exterior complaint did not arrive within the bounded probe window.");
                if (!moved) throw new InvalidOperationException("Resident did not physically move along the outdoor complaint route.");
                if (game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.Assault)
                    throw new InvalidOperationException("Arrival did not preserve the current direct-threat carrier charge.");
                bool physicalReport = false;
                foreach (var report in guards.Roster.Reports)
                    if (!oldReports.Contains(report.Id) && !report.BuildingKey.HasValue &&
                        report.GuardIds != null && report.GuardIds.Count > 0)
                    { physicalReport = true; break; }
                if (!physicalReport) throw new InvalidOperationException("Guard arrival did not dispatch a finite outdoor response at the remembered scene.");
                if (guards.Roster.Guards.Count != beforeRoster)
                    throw new InvalidOperationException("Dialogue complaint spawned extra guards.");
                completed = true;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_HOSTILITY_PROBE_PASS: receiptless dislike cannot report; controlled exact threat receipt; actual runner movement; delayed charge; original crime position; finite guard response; no guard spawning.");
            }
            finally
            {
                if (!completed) store.RemoveDialogueComplaint(personId);
                game.PlayerEntity.CrimeCommitted = originalCrime;
            }
            // The terminal indoor report assigns finite responders, so exercise it
            // only after the outdoor physical-arrival check has completed.
            TownSmokeHarness.EnterInterior(owned.DoorOwner, owned.EntryDoor);
            float enterDeadline = Time.realtimeSinceStartup + 45f;
            while ((!game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerEnterExit.Interior ||
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey != owned.BuildingKey) &&
                Time.realtimeSinceStartup < enterDeadline)
                yield return null;
            if (!game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerEnterExit.Interior ||
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey != owned.BuildingKey)
                throw new InvalidOperationException("Return to registered keeper's building timed out.");
            var keeper = FindFixtureKeeper(game, store, owned);
            float keeperDeadline = Time.realtimeSinceStartup + 30f;
            while (!keeper && Time.realtimeSinceStartup < keeperDeadline)
            {
                yield return null;
                keeper = FindFixtureKeeper(game, store, owned);
            }
            if (!keeper) throw new InvalidOperationException("Registered keeper was not ready after exterior transition.");
            OpenKeeperConversation(game, keeper);
            yield return WaitForOwnedTalk(adapter);
            RunOwnedPlaceTerminal(adapter);
            DaggerfallUI.Instance.PopToHUD();
            game.PlayerEnterExit.TransitionExterior();
            float finalExitDeadline = Time.realtimeSinceStartup + 45f;
            while ((game.PlayerEnterExit.IsPlayerInside || game.StreamingWorld.IsRepositioningPlayer ||
                !game.StreamingWorld.IsReady) && Time.realtimeSinceStartup < finalExitDeadline)
                yield return null;
            if (game.PlayerEnterExit.IsPlayerInside || game.StreamingWorld.IsRepositioningPlayer || !game.StreamingWorld.IsReady)
                throw new InvalidOperationException("Exterior transition after indoor report timed out.");
        }

        private static bool ProbeReady(GameManager game, PeopleOfTheIliacBayMod adapter, StreetPopulation streets,
            GuardPopulation guards, WorldStateStore store)
        {
            if (!adapter || !streets || !streets.Ready || !guards || !guards.OwnsSettlement || guards.Roster == null ||
                !guards.HasAvailableDialogueGuards() || store == null || !game || !game.IsReady || !game.PlayerGPS.HasCurrentLocation)
                return false;
            int settlement = game.PlayerGPS.CurrentLocation.MapTableData.MapId;
            var currentField = typeof(PeopleOfTheIliacBayMod).GetField("current", Private);
            var current = currentField == null ? null : currentField.GetValue(adapter) as SettlementPopulation;
            return current != null && current.SettlementId == settlement && store.NativePeopleFor(settlement).Count > 0;
        }

        private static OwnedFixture CaptureOwnedFixture(GameManager game, WorldStateStore store)
        {
            int settlement = game.PlayerGPS.CurrentLocation.MapTableData.MapId;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var selector = typeof(DispositionPricingProbe).GetMethod("SelectTavern", PrivateStatic);
            if (!directory || selector == null) throw new InvalidOperationException("Restored inn fixture selector is unavailable.");
            object[] args = { directory, null, default(StaticDoor), -1 };
            if (!(bool)selector.Invoke(null, args))
                throw new InvalidOperationException("Restored settlement has no registered keeper with a tavern door.");
            var fixture = new OwnedFixture { DoorOwner = (Transform)args[1], EntryDoor = (StaticDoor)args[2],
                BuildingKey = (int)args[3], SettlementId = settlement };
            if (!game.PlayerEnterExit.Interior || game.PlayerEnterExit.Interior.EntryDoor.buildingKey != fixture.BuildingKey)
                throw new InvalidOperationException("Pricing probe left a different building than the registered keeper fixture.");
            var record = store.NativePeopleFor(settlement).Find(p => p.IsAlive && p.WorkKey == fixture.BuildingKey);
            if (record == null) throw new InvalidOperationException("Current inn has no registered native owner after restore.");
            fixture.KeeperId = record.Id; fixture.KeeperHash = record.NativeHash;
            if (!FindFixtureKeeper(game, store, fixture))
                throw new InvalidOperationException("Registered keeper identity is not loaded in the restored inn interior.");
            return fixture;
        }

        private static StaticNPC FindFixtureKeeper(GameManager game, WorldStateStore store, OwnedFixture fixture)
        {
            if (!game.PlayerEnterExit.Interior || fixture == null) return null;
            var record = store.NativePeopleFor(fixture.SettlementId).Find(p => p.Id == fixture.KeeperId && p.IsAlive &&
                p.WorkKey == fixture.BuildingKey && p.NativeHash == fixture.KeeperHash);
            if (record == null) return null;
            foreach (var candidate in game.PlayerEnterExit.Interior.GetComponentsInChildren<StaticNPC>(true))
                if (candidate && candidate.gameObject.activeInHierarchy && candidate.Data.context == StaticNPC.Context.Building &&
                    candidate.Data.mapID == fixture.SettlementId && candidate.Data.buildingKey == fixture.BuildingKey &&
                    candidate.Data.hash == fixture.KeeperHash) return candidate;
            return null;
        }

        private static IEnumerator RunOwnedPlaceWarning(PeopleOfTheIliacBayMod adapter, GameManager game,
            WorldStateStore store, OwnedFixture fixture)
        {
            var keeper = FindFixtureKeeper(game, store, fixture);
            if (!keeper) throw new InvalidOperationException("Registered owner is unavailable for the owned-place warning check.");
            OpenKeeperConversation(game, keeper);
            yield return WaitForOwnedTalk(adapter);
            RunOwnedPlaceWarning(adapter);
            DaggerfallUI.Instance.PopToHUD();
        }

        private static void OpenKeeperConversation(GameManager game, StaticNPC keeper)
        {
            game.TalkManager.TalkToStaticNPC(keeper);
            var click = typeof(PlayerActivate).GetMethod("StaticNPCClick", Private);
            if (click == null) throw new InvalidOperationException("Native static NPC conversation handler is unavailable.");
            click.Invoke(game.PlayerActivate, new object[] { keeper });
        }

        private static IEnumerator WaitForOwnedTalk(PeopleOfTheIliacBayMod adapter)
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while ((!adapter || !(bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", Private).GetValue(adapter) ||
                !(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)) && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!adapter || !(bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", Private).GetValue(adapter) ||
                !(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow))
                throw new InvalidOperationException("Keeper Talk session did not initialize for exact-owner policy checks.");
        }

        // Call while Talk is open with the actual keeper in that keeper's own shop/inn.
        // It verifies runtime scene ownership and native-answer refusal without an LLM.
        public static void RunOwnedPlaceWarning(PeopleOfTheIliacBayMod adapter)
        {
            if (!adapter || !GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding)
                throw new InvalidOperationException("Owned-place policy probe requires an open owned-building Talk session.");
            var owns = typeof(PeopleOfTheIliacBayMod).GetMethod("IsConversationAtOwnedPlace", Private);
            var answer = typeof(PeopleOfTheIliacBayMod).GetMethod("NativeConversationAnswerOverride", Private);
            var reaction = typeof(TalkManager).GetField("reactionToPlayer", Private);
            if (!(bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", Private).GetValue(adapter))
                throw new InvalidOperationException("Owned-place warning probe requires an active Talk session.");
            var old = reaction.GetValue(GameManager.Instance.TalkManager);
            try
            {
                object[] args = { null };
                if (!(bool)owns.Invoke(adapter, args))
                    throw new InvalidOperationException("Current conversation identity is not the actual home/business owner.");
                reaction.SetValue(GameManager.Instance.TalkManager, -50);
                string response = (string)answer.Invoke(adapter, null);
                if (string.IsNullOrEmpty(response) || response.IndexOf("leave", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidOperationException("Owned-place factual answer did not warn/refuse at -50.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_HOSTILITY_OWNED_PLACE_PASS: exact home/work ownership; -50 native factual refusal.");
            }
            finally { reaction.SetValue(GameManager.Instance.TalkManager, old); }
        }

        public static void RunOwnedPlaceTerminal(PeopleOfTheIliacBayMod adapter)
        {
            if (!adapter || !GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding)
                throw new InvalidOperationException("Owned-place terminal probe requires an open owned-building Talk session.");
            var owns = typeof(PeopleOfTheIliacBayMod).GetMethod("IsConversationAtOwnedPlace", Private);
            object[] args = { null };
            if (!(bool)owns.Invoke(adapter, args))
                throw new InvalidOperationException("Current conversation identity is not the actual home/business owner.");
            var guards = adapter.GetComponent<GuardPopulation>();
            if (!guards || !guards.HasAvailableDialogueGuards())
                throw new InvalidOperationException("Owned-place terminal probe needs a live garrison.");
            var answer = typeof(PeopleOfTheIliacBayMod).GetMethod("NativeConversationAnswerOverride", Private);
            var reaction = typeof(TalkManager).GetField("reactionToPlayer", Private);
            var crime = GameManager.Instance.PlayerEntity.CrimeCommitted;
            var oldReaction = reaction.GetValue(GameManager.Instance.TalkManager);
            int reportCount=guards.Roster.Reports.Count;
            try
            {
                GameManager.Instance.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Theft;
                reaction.SetValue(GameManager.Instance.TalkManager, -100);
                string response = (string)answer.Invoke(adapter, null);
                if (response == null || response.IndexOf("do not wish to speak", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidOperationException("Terminal indoor dislike did not refuse further conversation.");
                if (GameManager.Instance.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.Theft)
                    throw new InvalidOperationException("Terminal indoor hostility replaced the stronger prior Theft charge.");
                if(guards.Roster.Reports.Count!=reportCount)
                    throw new InvalidOperationException("Terminal indoor dislike dispatched an unsupported report.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_HOSTILITY_INDOOR_PASS: -100 ended Talk; preserved Theft; no dislike-based dispatch.");
            }
            finally
            {
                GameManager.Instance.PlayerEntity.CrimeCommitted = crime;
                reaction.SetValue(GameManager.Instance.TalkManager, oldReaction);
            }
        }
    }
}
