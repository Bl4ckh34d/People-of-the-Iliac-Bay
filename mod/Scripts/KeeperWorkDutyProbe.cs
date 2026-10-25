using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // No-model native acceptance for exact, transient tavern-keeper work context.
    internal static class KeeperWorkDutyProbe
    {
        internal const string Flag = "-peopleoftheiliacbay-keeper-work-duty-probe";
        private const string OwnedCharacter = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string BaselineSave = "People - Gothway Garden";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static IEnumerator RunKeeperWorkDuty(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            GameManager game = GameManager.Instance;
            if (!host || !streets || !game || character != OwnedCharacter ||
                game.PlayerEntity == null || game.PlayerEntity.Name != OwnedCharacter ||
                ArgumentValue("-peopleoftheiliacbay-fixture-character") != OwnedCharacter ||
                ArgumentValue("-peopleoftheiliacbay-fixture-save") != BaselineSave)
                throw new InvalidOperationException("Keeper work probe requires exact owned character and Gothway Garden baseline.");

            game.SaveLoadManager.EnumerateSaves();
            if (game.SaveLoadManager.FindSaveFolderByNames(OwnedCharacter, BaselineSave) < 0)
                throw new InvalidOperationException("Keeper work probe baseline save is missing: " + BaselineSave + ".");

            var setup = new NestedEnumeratorDriver(ConversationMoodProbe.LoadSave(host, streets, OwnedCharacter, BaselineSave, true));
            try
            {
                object yielded;
                while (setup.TryMoveNext(out yielded)) yield return yielded;
            }
            finally { setup.Dispose(); }
            if (setup.Failure != null) throw new InvalidOperationException("Keeper work probe could not load exact baseline.", setup.Failure);

            Exception probeFailure = null;
            Exception cleanupFailure = null;
            var probe = new NestedEnumeratorDriver(RunCore(host, streets));
            bool coreCompleted = false;
            try
            {
                object yielded;
                while (probe.TryMoveNext(out yielded)) yield return yielded;
                probeFailure = probe.Failure;
                coreCompleted = true;
            }
            finally
            {
                probe.Dispose();
                if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
                if (!coreCompleted)
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WORK_BASELINE_RESTORE_SKIPPED_EXTERNAL_CANCEL: Talk closed synchronously; iterator cancellation cannot yield save reload.");
            }
            if (!coreCompleted) yield break;

            var cleanup = new NestedEnumeratorDriver(RestoreBaseline(host, streets));
            bool cleanupFinished = false;
            try
            {
                object yielded;
                while (cleanup.TryMoveNext(out yielded)) yield return yielded;
                cleanupFailure = cleanup.Failure;
                cleanupFinished = true;
            }
            finally
            {
                cleanup.Dispose();
                if (!cleanupFinished)
                {
                    if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WORK_BASELINE_RESTORE_INTERRUPTED: Talk closed; asynchronous save reload did not finish.");
                }
            }

            if (cleanupFailure == null)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WORK_BASELINE_RESTORED: character=" + OwnedCharacter + "; save=" + BaselineSave + ".");
            if (probeFailure != null && cleanupFailure != null)
                throw new AggregateException("Keeper work probe failed and baseline restoration also failed.", probeFailure, cleanupFailure);
            if (probeFailure != null) throw probeFailure;
            if (cleanupFailure != null) throw cleanupFailure;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WORK_PASS: real tavern keeper duty, same-venue patron exclusion, stale-session rejection, no model request.");
        }

        private static IEnumerator RunCore(PeopleOfTheIliacBayMod host, StreetPopulation streets)
        {
            GameManager game = GameManager.Instance;
            if (!game || !game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.Name != "Gothway Garden" ||
                game.PlayerEnterExit.IsPlayerInsideBuilding)
                throw new InvalidOperationException("Keeper work probe must start from the loaded Gothway Garden exterior baseline.");
            WorldStateStore store = ReadStore(host);
            if (store == null) throw new InvalidOperationException("Keeper work probe cannot read native fixture store.");

            BuildingDirectory directory = game.StreamingWorld == null ? null : game.StreamingWorld.GetCurrentBuildingDirectory();
            if (!directory) throw new InvalidOperationException("Keeper work probe has no current native Gothway directory.");
            DaggerfallStaticDoors entranceOwner = null;
            StaticDoor entrance = default(StaticDoor);
            foreach (DaggerfallStaticDoors doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                foreach (StaticDoor door in doors.Doors)
                {
                    BuildingSummary summary;
                    if (door.doorType != DoorTypes.Building || !directory.GetBuildingSummary(door.buildingKey, out summary) ||
                        summary.BuildingType != DFLocation.BuildingTypes.Tavern) continue;
                    entranceOwner = doors;
                    entrance = door;
                    break;
                }
                if (entranceOwner) break;
            }
            if (!entranceOwner)
                throw new InvalidOperationException("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WORK_UNAVAILABLE: no real Gothway Tavern entrance.");

            TownSmokeHarness.EnterInterior(entranceOwner.transform, entrance);
            float deadline = Time.realtimeSinceStartup + 35f;
            while ((!game.PlayerEnterExit.IsPlayerInsideBuilding || game.PlayerEnterExit.BuildingType != DFLocation.BuildingTypes.Tavern) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            if (!game.PlayerEnterExit.IsPlayerInsideBuilding || game.PlayerEnterExit.BuildingType != DFLocation.BuildingTypes.Tavern)
                throw new InvalidOperationException("Native Tavern entrance did not complete ordinary interior transition.");

            DaggerfallInterior room = game.PlayerEnterExit.Interior;
            if (!room) throw new InvalidOperationException("Native Tavern transition has no interior component.");
            StaticNPC keeper = null;
            StaticNPC patron = null;
            float registrationDeadline = Time.realtimeSinceStartup + 15f;
            while ((!keeper || !patron) && Time.realtimeSinceStartup < registrationDeadline)
            {
                keeper = FindSpeaker(room, store, directory.MapID, entrance.buildingKey, true);
                patron = FindSpeaker(room, store, directory.MapID, entrance.buildingKey, false);
                if (!keeper || !patron) yield return null;
            }
            if (!keeper)
                throw new InvalidOperationException("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WORK_UNAVAILABLE: no current registered native Tavern keeper.");
            if (!patron)
                throw new InvalidOperationException("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WORK_UNAVAILABLE: no same-venue registered native non-keeper patron.");
            if (StaticPersonId(keeper) == StaticPersonId(patron) || keeper.Data.mapID != patron.Data.mapID ||
                keeper.Data.buildingKey != patron.Data.buildingKey || NativeBusinessServices.IsKeeper(patron, DFLocation.BuildingTypes.Tavern))
                throw new InvalidOperationException("Keeper/patron native fixture identities or venue do not satisfy exact-role contract.");

            HashSet<string> ownersBefore = MotivationOwners(store);
            ConversationContextSnapshot keeperSnapshot = null;
            DaggerfallTalkWindow keeperTalk = null;
            yield return OpenTalk(game, host, keeper, talk => keeperTalk = talk);
            try
            {
                keeperSnapshot = CaptureBoundSnapshot(host, keeperTalk, keeper, store);
                NpcMotivationDuty keeperDuty = AssertKeeperDuty(keeperSnapshot, keeper, game);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WORK_SNAPSHOT: person=" + keeperSnapshot.Binding.PersonId +
                    "; site=" + keeperDuty.Evidence.EvidenceId +
                    "; capturedGameTime=" + keeperSnapshot.CapturedGameTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ".");
            }
            finally
            {
                DaggerfallUI.Instance.PopToHUD();
            }
            yield return WaitForClosed(host);

            DaggerfallTalkWindow patronTalk = null;
            yield return OpenTalk(game, host, patron, talk => patronTalk = talk);
            try
            {
                if (host.IsCurrentConversationContext(keeperSnapshot, patronTalk))
                    throw new InvalidOperationException("Prior keeper snapshot remained current after native speaker changed to same-venue patron.");
                AssertOldSnapshotMemoryRejected(host, keeperSnapshot, patronTalk);
                ConversationContextSnapshot patronSnapshot = CaptureBoundSnapshot(host, patronTalk, patron, store);
                AssertNoKeeperDuty(patronSnapshot, patron, keeper);
                if (host.IsCurrentConversationContext(keeperSnapshot, patronTalk) || !host.IsCurrentConversationContext(patronSnapshot, patronTalk))
                    throw new InvalidOperationException("Native speaker change did not reject keeper context and accept the current patron snapshot.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WORK_PATRON: person=" + patronSnapshot.Binding.PersonId + "; workDuty=absent; staleKeeperSnapshot=rejected.");
            }
            finally
            {
                DaggerfallUI.Instance.PopToHUD();
            }
            yield return WaitForClosed(host);

            AssertMotivationOwnersUnchanged(store, ownersBefore);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WORK_READ_ONLY: savedMotivationRowsAdded=0; no model request.");
        }

        private static StaticNPC FindSpeaker(DaggerfallInterior room, WorldStateStore store, int settlement, int building, bool keeperRequired)
        {
            foreach (StaticNPC npc in room.GetComponentsInChildren<StaticNPC>(true))
            {
                if (!npc || !npc.gameObject.activeInHierarchy || npc.IsChildNPC || npc.Data.context != StaticNPC.Context.Building ||
                    npc.Data.mapID != settlement || npc.Data.buildingKey != building || npc.GetComponent<QuestResourceBehaviour>() ||
                    npc.GetComponent<DaggerfallAction>() || NativeBusinessServices.IsKeeper(npc, DFLocation.BuildingTypes.Tavern) != keeperRequired ||
                    GameManager.Instance.TalkManager.IsNpcOfferingQuest(npc.Data.nameSeed) ||
                    GameManager.Instance.TalkManager.IsCastleNpcOfferingQuest(npc.Data.nameSeed)) continue;
                string personId = StaticPersonId(npc);
                NativePerson native = store.NativePeopleFor(settlement).Find(x => x != null && x.Id == personId && x.IsAlive &&
                    x.WorkKey == building && x.NativeHash == npc.Data.hash && x.FactionId == npc.Data.factionID);
                if (native == null) continue;
                // CaptureStablePersonality creates the saved profile on first contact.
                // Requiring it before opening Talk would exclude legitimate new speakers.
                // Deliberately do not require an empty conversation archive: historical
                // dialogue does not determine current native work hours or site ownership.
                return npc;
            }
            return null;
        }

        private static IEnumerator OpenTalk(GameManager game, PeopleOfTheIliacBayMod host, StaticNPC npc,
            Action<DaggerfallTalkWindow> onReady)
        {
            DaggerfallUI.Instance.PopToHUD();
            game.TalkManager.TalkToStaticNPC(npc, false);
            float deadline = Time.realtimeSinceStartup + 25f;
            bool initializedReadOnlySession = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                DaggerfallTalkWindow talk = DaggerfallUI.UIManager == null ? null : DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                if (talk != null && talk.IsSetup && game.TalkManager.CurrentNPCType == TalkManager.NPCType.Static &&
                    game.TalkManager.StaticNPC == npc)
                {
                    // AI-off UpdateEmbeddedDialogue deliberately does not begin a model session.
                    // Exercise only the existing context/memory setup, without requesting a reply.
                    if (!SessionIsBound(host, talk, npc) && !initializedReadOnlySession)
                    {
                        MethodInfo begin = typeof(PeopleOfTheIliacBayMod).GetMethod("BeginGroundedConversation", PrivateInstance);
                        if (begin == null) throw new InvalidOperationException("Native conversation context setup method is missing.");
                        begin.Invoke(host, null);
                        initializedReadOnlySession = true;
                    }
                    if (SessionIsBound(host, talk, npc))
                    {
                        onReady(talk);
                        yield break;
                    }
                }
                yield return null;
            }
            throw new InvalidOperationException("Native TalkToStaticNPC did not bind the selected keeper/patron.");
        }

        private static ConversationContextSnapshot CaptureBoundSnapshot(PeopleOfTheIliacBayMod host,
            DaggerfallTalkWindow talk, StaticNPC npc, WorldStateStore store)
        {
            if (!SessionIsBound(host, talk, npc))
                throw new InvalidOperationException("Native Talk session no longer owns the selected keeper/patron.");
            FieldInfo revision = typeof(PeopleOfTheIliacBayMod).GetField("conversationContextRequestRevision", PrivateInstance);
            if (revision == null) throw new InvalidOperationException("Native conversation request revision is unavailable.");
            long requestRevision = checked(Convert.ToInt64(revision.GetValue(host)) + 1L);
            ConversationContextSnapshot snapshot = host.CaptureConversationContext(talk,
                "Controlled keeper-work snapshot check. No model request is sent.", requestRevision);
            LogPersonalityDiagnostic(host, store, npc, snapshot);
            ConversationDialogueRole capturedRole;
            if (!host.IsCurrentConversationContext(snapshot, talk) || snapshot.Binding.PersonId != StaticPersonId(npc) ||
                snapshot.Role == null || (snapshot.Role.TryGetVerified(out capturedRole) &&
                    (snapshot.Role.Source != ConversationContextFactSource.NativeActor ||
                     snapshot.Role.ObservedAt != snapshot.CapturedGameTime)))
                throw new InvalidOperationException("Native keeper/patron context is not exact, current, and actor-bound.");
            if (snapshot.MotivationContext == null || snapshot.MotivationContext.Profile == null ||
                snapshot.MotivationContext.ObserverId != snapshot.Binding.PersonId ||
                snapshot.MotivationContext.SaveBranchId != snapshot.MotivationBranchId)
                throw new InvalidOperationException("Native keeper/patron snapshot lacks saved-profile-bound motivation context.");
            return snapshot;
        }

        private static void LogPersonalityDiagnostic(PeopleOfTheIliacBayMod host,
            WorldStateStore store, StaticNPC npc, ConversationContextSnapshot snapshot)
        {
            string personId = StaticPersonId(npc);
            NativePerson native = store.NativePeopleFor(npc.Data.mapID).Find(x => x != null && x.Id == personId);
            var profile = store.ReadNpcPersonality(personId);
            FieldInfo currentPerson = typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId", PrivateInstance);
            MethodInfo seedMethod = typeof(PeopleOfTheIliacBayMod).GetMethod("TryNativePersonalitySeed", PrivateInstance);
            MethodInfo backgroundMethod = typeof(PeopleOfTheIliacBayMod).GetMethod("TryResolveNpcPersonalityBackground",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (currentPerson == null || seedMethod == null || backgroundMethod == null)
                throw new InvalidOperationException("Native personality diagnostic methods are unavailable.");
            object seed = seedMethod.Invoke(host, new object[] { npc.Data.mapID, npc.Data.buildingKey, npc.Data.hash });
            object[] backgroundArguments = { npc.Data.factionID, NpcSocialBackground.Unknown };
            bool backgroundResolved = (bool)backgroundMethod.Invoke(null, backgroundArguments);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WORK_PERSONALITY_DIAGNOSTIC: person=" + personId +
                "; conversationPerson=" + (currentPerson.GetValue(host) as string) +
                "; nativeFound=" + (native != null) + "; nativeWorkKey=" + (native == null ? -1 : native.WorkKey) +
                "; nativeHash=" + (native == null ? 0 : native.NativeHash) + "; actualHash=" + npc.Data.hash +
                "; factionId=" + npc.Data.factionID + "; nativeFactionId=" + (native == null ? -1 : native.FactionId) +
                "; seedPresent=" + (seed != null) + "; seed=" + Convert.ToString(seed, System.Globalization.CultureInfo.InvariantCulture) +
                "; backgroundResolved=" + backgroundResolved + "; background=" + backgroundArguments[1] +
                "; profilePresent=" + (profile != null) + "; profileVersion=" + (profile == null ? -1 : profile.Version) +
                "; motivationPresent=" + (snapshot != null && snapshot.MotivationContext != null) + ".");
        }

        private static NpcMotivationDuty AssertKeeperDuty(ConversationContextSnapshot snapshot, StaticNPC keeper, GameManager game)
        {
            ConversationDialogueRole role;
            if (!snapshot.Role.TryGetVerified(out role) || role != ConversationDialogueRole.Innkeeper)
                throw new InvalidOperationException("Real Tavern keeper lacks exact native Innkeeper role.");
            BuildingDirectory directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            BuildingSummary summary;
            if (!directory || directory.MapID != keeper.Data.mapID ||
                !directory.GetBuildingSummary(keeper.Data.buildingKey, out summary) || summary.BuildingType != DFLocation.BuildingTypes.Tavern)
                throw new InvalidOperationException("Keeper Work duty source differs from actual native Tavern site.");
            int type = (int)summary.BuildingType;
            if (type < 0 || type >= PlayerActivate.openHours.Length || type >= PlayerActivate.closeHours.Length)
                throw new InvalidOperationException("DFU Tavern hours are unavailable.");
            int openHour = PlayerActivate.openHours[type];
            int closeHour = Math.Min(24, (int)PlayerActivate.closeHours[type]);
            if (openHour < 0 || openHour >= closeHour || closeHour > 24)
                throw new InvalidOperationException("DFU Tavern hours are invalid.");
            double now = snapshot.CapturedGameTime;
            double dayStart = Math.Floor(now / 86400d) * 86400d;
            double starts = dayStart + openHour * 3600d;
            double ends = dayStart + closeHour * 3600d;
            if (now < starts || now >= ends)
                throw new InvalidOperationException("Keeper work probe ran outside actual DFU Tavern hours; rerun during open hours.");
            string siteId = "building:" + keeper.Data.mapID.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                keeper.Data.buildingKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string dutyId = "work:" + snapshot.Binding.PersonId + ":" + siteId;
            string workEvidenceId = "native-work:" + siteId;
            int count = 0;
            NpcMotivationDuty matchedDuty = null;
            foreach (NpcMotivationDuty duty in snapshot.MotivationContext.Duties)
            {
                if (duty == null || duty.Kind != NpcMotivationDutyKind.Work || duty.Evidence == null ||
                    duty.Evidence.Kind != NpcMotivationEvidenceKind.NativeObservation ||
                    duty.Evidence.EvidenceId != workEvidenceId) continue;
                count++;
                if (duty.Id != dutyId || duty.Scope != (NpcMotivationDutyScope.RoutineWork | NpcMotivationDutyScope.Schedule) ||
                    duty.Status != NpcMotivationDutyStatus.Active || duty.KnowledgeState != NpcMotivationKnowledgeState.Known ||
                    duty.StartsAtGameTime != starts || !duty.EndsAtGameTime.HasValue || duty.EndsAtGameTime.Value != ends ||
                    duty.Revision != snapshot.Binding.RequestRevision ||
                    duty.Evidence.SaveBranchId != snapshot.MotivationBranchId || duty.Evidence.ObserverId != snapshot.Binding.PersonId ||
                    duty.Evidence.Revision != snapshot.Binding.RequestRevision ||
                    duty.Evidence.ObservedGameTime != now)
                    throw new InvalidOperationException("Tavern Work duty mismatched native ID/site, DFU hours, request revision, or observation provenance.");
                matchedDuty = duty;
            }
            if (count != 1) throw new InvalidOperationException("Expected exactly one Work duty for current Tavern keeper; found " + count + ".");
            return matchedDuty;
        }

        private static void AssertNoKeeperDuty(ConversationContextSnapshot snapshot, StaticNPC patron, StaticNPC keeper)
        {
            ConversationDialogueRole role;
            if (snapshot.Role.TryGetVerified(out role) ||
                snapshot.Binding.PersonId != StaticPersonId(patron))
                throw new InvalidOperationException("Same-venue non-keeper patron role is not truthfully Unknown or identity does not match.");
            string venueEvidenceId = "native-work:building:" + keeper.Data.mapID.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                keeper.Data.buildingKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (NpcMotivationDuty duty in snapshot.MotivationContext.Duties)
                if (duty != null && duty.Kind == NpcMotivationDutyKind.Work && duty.Evidence != null && duty.Evidence.EvidenceId == venueEvidenceId)
                    throw new InvalidOperationException("Same-venue non-keeper patron received the keeper's native Tavern Work duty.");
        }

        private static void AssertOldSnapshotMemoryRejected(PeopleOfTheIliacBayMod host,
            ConversationContextSnapshot oldSnapshot, DaggerfallTalkWindow newTalk)
        {
            if (oldSnapshot == null || host.IsCurrentConversationContext(oldSnapshot, newTalk))
                throw new InvalidOperationException("Old keeper context was accepted for current patron Talk window.");
            string memory = host.ReadCapturedConversationMemory(oldSnapshot, newTalk);
            if (!string.IsNullOrEmpty(memory))
                throw new InvalidOperationException("Captured keeper memory was readable from different patron Talk session.");
        }

        private static bool SessionIsBound(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk, StaticNPC npc)
        {
            FieldInfo open = typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", PrivateInstance);
            FieldInfo window = typeof(PeopleOfTheIliacBayMod).GetField("memoryWindow", PrivateInstance);
            FieldInfo speaker = typeof(PeopleOfTheIliacBayMod).GetField("conversationContextSessionSpeaker", PrivateInstance);
            return talk != null && DaggerfallUI.UIManager != null && DaggerfallUI.UIManager.TopWindow == talk && talk.IsSetup &&
                open != null && window != null && speaker != null && Convert.ToBoolean(open.GetValue(host)) &&
                ReferenceEquals(window.GetValue(host), talk) && ReferenceEquals(speaker.GetValue(host), npc);
        }

        private static IEnumerator WaitForClosed(PeopleOfTheIliacBayMod host)
        {
            FieldInfo session = typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", PrivateInstance);
            float deadline = Time.realtimeSinceStartup + 8f;
            while (session != null && Convert.ToBoolean(session.GetValue(host)) && Time.realtimeSinceStartup < deadline) yield return null;
            if (session != null && Convert.ToBoolean(session.GetValue(host)))
                throw new InvalidOperationException("Native keeper/patron Talk session did not close.");
        }

        private static IEnumerator RestoreBaseline(PeopleOfTheIliacBayMod host, StreetPopulation streets)
        {
            if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
            yield return ConversationMoodProbe.CloseTalk(host);
            yield return ConversationMoodProbe.LoadSave(host, streets, OwnedCharacter, BaselineSave, true);
            GameManager game = GameManager.Instance;
            if (!game || game.PlayerEntity == null || game.PlayerEntity.Name != OwnedCharacter ||
                !game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.Name != "Gothway Garden" ||
                game.PlayerEnterExit.IsPlayerInsideBuilding)
                throw new InvalidOperationException("Restored keeper-work baseline does not match exact exterior fixture.");
        }

        private static string StaticPersonId(StaticNPC npc) => "npc:" +
            npc.Data.mapID.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
            npc.Data.buildingKey.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
            npc.Data.hash.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static HashSet<string> MotivationOwners(WorldStateStore store)
        {
            var owners = new HashSet<string>(StringComparer.Ordinal);
            WorldState state = store.Snapshot();
            if (state.NpcMotivations != null)
                foreach (NpcMotivationState row in state.NpcMotivations)
                    if (row != null && !string.IsNullOrWhiteSpace(row.OwnerPersonId)) owners.Add(row.OwnerPersonId);
            return owners;
        }

        private static void AssertMotivationOwnersUnchanged(WorldStateStore store, HashSet<string> before)
        {
            if (before == null || !before.SetEquals(MotivationOwners(store)))
                throw new InvalidOperationException("Keeper and patron snapshot capture added or removed persisted motivation rows.");
        }

        private static WorldStateStore ReadStore(PeopleOfTheIliacBayMod host)
        {
            FieldInfo field = typeof(PeopleOfTheIliacBayMod).GetField("store", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(host) as WorldStateStore;
        }

        private static string ArgumentValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        private sealed class NestedEnumeratorDriver : IDisposable
        {
            private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
            private bool complete;
            internal Exception Failure { get; private set; }
            internal bool IsComplete { get { return complete; } }
            internal NestedEnumeratorDriver(IEnumerator root) { stack.Push(root); }
            internal bool TryMoveNext(out object yielded)
            {
                yielded = null;
                while (!complete && Failure == null && stack.Count > 0)
                {
                    IEnumerator top = stack.Peek();
                    bool more;
                    object current = null;
                    try { more = top.MoveNext(); if (more) current = top.Current; }
                    catch (Exception error) { Failure = error; DisposeStack(); return false; }
                    if (!more)
                    {
                        stack.Pop();
                        try { (top as IDisposable)?.Dispose(); }
                        catch (Exception error) { Failure = error; DisposeStack(); return false; }
                        continue;
                    }
                    IEnumerator child = current as IEnumerator;
                    if (child != null) { stack.Push(child); continue; }
                    yielded = current;
                    return true;
                }
                if (stack.Count == 0) complete = true;
                return false;
            }
            private void DisposeStack()
            {
                while (stack.Count > 0) try { (stack.Pop() as IDisposable)?.Dispose(); } catch { }
                complete = true;
            }
            public void Dispose() { DisposeStack(); }
        }
    }
}
