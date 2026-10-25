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
using DaggerfallWorkshop.Game.Weather;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeopleOfTheIliacBay.DFU
{
    // Dormant native acceptance probe for live NPC category, identity, role,
    // and truthful prompt projection. It never submits a model request or saves.
    internal static class ConversationContextSpeakerProbe
    {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string ManagedPass = "PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SPEAKER_PASS: managed-mobile";
        private const string GuardPass = "PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SPEAKER_PASS: unmanaged-guard";
        private const string MobilePass = "PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SPEAKER_PASS: unmanaged-mobile";
        private const string KeeperPass = "PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SPEAKER_PASS: static-keeper";
        private const string UnknownStaticPass = "PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SPEAKER_PASS: static-unknown-role";

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            if (!host || !streets || string.IsNullOrEmpty(character) ||
                !NativeProbeText.IsTestCharacter(character))
                throw new InvalidOperationException("Speaker probe requires an explicitly owned disposable fixture character.");

            GameManager game = GameManager.Instance;
            if (game == null || !game.IsReady || game.PlayerMotor == null || game.PlayerEntity == null ||
                !string.Equals(game.PlayerEntity.Name, character, StringComparison.Ordinal))
                throw new InvalidOperationException("Speaker probe requires the requested owned fixture to be loaded in a ready game.");
            if (game.PlayerGPS.CurrentLocation.Name != "Gothway Garden" || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Speaker probe requires the owned Gothway Garden exterior fixture.");

            string fixtureSave = CommandLineValue("-peopleoftheiliacbay-fixture-save");
            if (!string.IsNullOrEmpty(fixtureSave) && fixtureSave != "People - Gothway Garden" &&
                fixtureSave != "People - Shop entrance" && fixtureSave != "People of the Iliac Bay - inside Gothway Garden")
                throw new InvalidOperationException("Speaker probe accepts only the owned Gothway Garden or Shop entrance fixtures.");

            WorldStateStore store = ReadStore(host);
            if (store == null) throw new InvalidOperationException("Speaker probe cannot read the restored disposable fixture store.");

            var unavailable = new List<string>();
            ResidentActor managed = null;
            float mobileFixtureDeadline = Time.realtimeSinceStartup + 45f;
            while (!managed && Time.realtimeSinceStartup < mobileFixtureDeadline)
            {
                managed = FindManagedSpeaker(streets, store, game);
                if (!managed) yield return new WaitForSecondsRealtime(0.25f);
            }
            if (!managed) RecordUnavailable(unavailable, "managed adult mobile resident with no saved transcript");
            else yield return ProbeMobile(host, streets, store, game, managed.Person, managed.Resident.Id,
                    ConversationActorCategory.NativeMobile, ConversationDialogueRole.ManagedResident, ManagedPass, false);

            MobilePersonNPC guard = null;
            mobileFixtureDeadline = Time.realtimeSinceStartup + 20f;
            while (!guard && Time.realtimeSinceStartup < mobileFixtureDeadline)
            {
                guard = FindUnmanagedMobile(game, true);
                if (!guard) yield return new WaitForSecondsRealtime(0.25f);
            }
            if (!guard) RecordUnavailable(unavailable, "active native unmanaged guard");
            else yield return ProbeMobile(host, streets, store, game, guard, null,
                    ConversationActorCategory.Guard, ConversationDialogueRole.Guard, GuardPass, true);

            MobilePersonNPC mobile = null;
            mobileFixtureDeadline = Time.realtimeSinceStartup + 20f;
            while (!mobile && Time.realtimeSinceStartup < mobileFixtureDeadline)
            {
                mobile = FindUnmanagedMobile(game, false);
                if (!mobile) yield return new WaitForSecondsRealtime(0.25f);
            }
            if (!mobile) RecordUnavailable(unavailable, "active native unmanaged non-guard mobile");
            else yield return ProbeMobile(host, streets, store, game, mobile, null,
                    ConversationActorCategory.Mobile, null, MobilePass, true);

            yield return EnterNativeGeneralStore(game);
            DaggerfallInterior room = game.PlayerEnterExit.Interior;
            if (!room || !game.PlayerEnterExit.IsPlayerInsideBuilding ||
                game.PlayerEnterExit.BuildingType != DFLocation.BuildingTypes.GeneralStore)
                throw new InvalidOperationException("Native shop-entry fixture did not reach a GeneralStore interior.");

            StaticNPC keeper = FindStaticSpeaker(room, game, store, true);
            StaticNPC unknown = FindStaticSpeaker(room, game, store, false);
            if (!keeper) RecordUnavailable(unavailable, "actual GeneralStore static keeper without saved dialogue");
            if (!unknown) RecordUnavailable(unavailable, "actual GeneralStore static non-keeper without saved dialogue");
            if (keeper && unknown)
            {
                if (string.Equals(StaticPersonId(keeper), StaticPersonId(unknown), StringComparison.Ordinal))
                    throw new InvalidOperationException("Keeper and unknown-role fixture resolved to the same native identity.");
                if (keeper.Data.mapID != unknown.Data.mapID || keeper.Data.buildingKey != unknown.Data.buildingKey)
                    throw new InvalidOperationException("Keeper and patron fixtures do not share the same native venue.");

                var motivationOwnersBefore = MotivationOwners(store);
                yield return ProbeStatic(host, streets, store, game, keeper, unknown,
                    ConversationActorCategory.Static, ConversationDialogueRole.Shopkeeper, KeeperPass);
                yield return ProbeStatic(host, streets, store, game, unknown, keeper,
                    ConversationActorCategory.Static, null, UnknownStaticPass);
                AssertMotivationOwnersUnchanged(store, motivationOwnersBefore);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SPEAKER_PASS: keeper work duty was snapshot-only; no motivation rows added.");
            }

            if (unavailable.Count > 0)
                throw new InvalidOperationException("PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SPEAKER_SUITE_INCOMPLETE: " + string.Join("; ", unavailable.ToArray()));

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SPEAKER_SUITE_PASS: managed, guard, unmanaged mobile, native keeper, and unknown-role static snapshots.");
        }

        private static IEnumerator ProbeMobile(PeopleOfTheIliacBayMod host, StreetPopulation streets, WorldStateStore store,
            GameManager game, MobilePersonNPC person, string expectedPersonId, ConversationActorCategory category,
            ConversationDialogueRole? expectedRole, string marker, bool unmanaged)
        {
            if (!person || !person.gameObject.activeInHierarchy || person.gameObject.scene != game.PlayerMotor.gameObject.scene)
                throw new InvalidOperationException("Selected native mobile left the active Gothway scene before Talk.");
            if (unmanaged && (person.GetComponent<ResidentActor>() || person.IsGuard != (category == ConversationActorCategory.Guard)))
                throw new InvalidOperationException("Selected unmanaged mobile changed category before Talk.");
            if (!unmanaged && (person.GetComponent<ResidentActor>() == null ||
                person.GetComponent<ResidentActor>().Resident == null ||
                !string.Equals(person.GetComponent<ResidentActor>().Resident.Id, expectedPersonId, StringComparison.Ordinal)))
                throw new InvalidOperationException("Selected managed mobile lost its exact resident binding.");

            CloseToHud();
            game.TalkManager.TalkToMobileNPC(person);
            DaggerfallTalkWindow talk = null;
            float deadline = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < deadline)
            {
                talk = BoundTalk(host, game, person, null);
                if (talk != null) break;
                yield return null;
            }
            if (talk == null) throw new InvalidOperationException("Native public TalkToMobileNPC did not open and bind the exact selected actor.");

            try
            {
                ConversationContextSnapshot snapshot = CaptureAndAssert(host, streets, game, talk, person,
                    expectedPersonId, category, expectedRole, unmanaged);
                AssertPromptProjection(host, snapshot, talk, game, unmanaged);
                Debug.Log(marker + ": personId=" + (snapshot.Binding.PersonId ?? "<ephemeral>") +
                    "; role=" + (expectedRole.HasValue ? expectedRole.Value.ToString() : "Unknown") + "; no model request.");
            }
            finally { CloseToHud(); }
            yield return WaitForSessionClose(host);
        }

        private static IEnumerator ProbeStatic(PeopleOfTheIliacBayMod host, StreetPopulation streets, WorldStateStore store,
            GameManager game, StaticNPC person, StaticNPC otherVenueSpeaker, ConversationActorCategory category,
            ConversationDialogueRole? expectedRole, string marker)
        {
            if (!person || game.PlayerEnterExit.Interior == null ||
                person.transform.gameObject.scene != game.PlayerEnterExit.Interior.gameObject.scene)
                throw new InvalidOperationException("Selected static NPC is not in the active native shop interior.");
            bool isKeeper = NativeBusinessServices.IsKeeper(person, DFLocation.BuildingTypes.GeneralStore);
            if (isKeeper != (expectedRole == ConversationDialogueRole.Shopkeeper) ||
                person.IsChildNPC || person.Data.context != StaticNPC.Context.Building ||
                person.GetComponent<QuestResourceBehaviour>() || person.GetComponent<DaggerfallAction>() ||
                game.TalkManager.IsNpcOfferingQuest(person.Data.nameSeed) ||
                game.TalkManager.IsCastleNpcOfferingQuest(person.Data.nameSeed))
                throw new InvalidOperationException("Static NPC no longer meets its exact keeper/non-keeper or quest-free fixture contract.");

            CloseToHud();
            game.TalkManager.TalkToStaticNPC(person, false);
            DaggerfallTalkWindow talk = null;
            float deadline = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < deadline)
            {
                talk = BoundTalk(host, game, null, person);
                if (talk != null) break;
                yield return null;
            }
            if (talk == null) throw new InvalidOperationException("Native public TalkToStaticNPC did not open and bind the exact selected actor.");

            try
            {
                string id = StaticPersonId(person);
                var motivationOwnersBefore = MotivationOwners(store);
                ConversationContextSnapshot snapshot = CaptureAndAssert(host, streets, game, talk, person,
                    id, category, expectedRole, expectedRole == null);
                if (expectedRole == ConversationDialogueRole.Shopkeeper)
                    AssertNativeKeeperWorkDuty(snapshot, person, game);
                else
                    AssertNoKeeperWorkDuty(snapshot, id);

                AssertWrongStaticSpeakerRejected(host, streets, game, talk, otherVenueSpeaker);
                ConversationContextSnapshot newer = CaptureAndAssert(host, streets, game, talk, person,
                    id, category, expectedRole, expectedRole == null);
                if (host.IsCurrentConversationContext(snapshot, talk) || !host.IsCurrentConversationContext(newer, talk))
                    throw new InvalidOperationException("A superseded static speaker snapshot remained current, or its successor was not current.");
                AssertMotivationOwnersUnchanged(store, motivationOwnersBefore);

                snapshot = newer;
                AssertPromptProjection(host, snapshot, talk, game, expectedRole == null);
                Debug.Log(marker + ": personId=" + id + "; role=" +
                    (expectedRole.HasValue ? expectedRole.Value.ToString() : "Unknown") +
                    "; workDuty=" + (expectedRole == ConversationDialogueRole.Shopkeeper ? "verified" : "absent") +
                    "; no model request.");
            }
            finally { CloseToHud(); }
            yield return WaitForSessionClose(host);
        }

        private static ConversationContextSnapshot CaptureAndAssert(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            GameManager game, DaggerfallTalkWindow talk, UnityEngine.Object actor, string expectedPersonId,
            ConversationActorCategory expectedCategory, ConversationDialogueRole? expectedRole, bool unknownActivity)
        {
            if (talk == null || !talk.IsSetup || DaggerfallUI.UIManager.TopWindow != talk ||
                !SessionIsBound(host, talk, actor, expectedPersonId))
                throw new InvalidOperationException("Snapshot capture refused because the native Talk window/session does not own the selected actor.");

            FieldInfo revisionField = typeof(PeopleOfTheIliacBayMod).GetField("conversationContextRequestRevision", InstanceMembers);
            if (revisionField == null) throw new InvalidOperationException("Native request revision is unavailable.");
            long revision = checked(Convert.ToInt64(revisionField.GetValue(host)) + 1L);
            string probeText = "IMM-02 native speaker snapshot probe; no dialogue request is sent.";
            ConversationContextSnapshot snapshot = host.CaptureConversationContext(talk, probeText, revision);
            if (snapshot == null || !host.IsCurrentConversationContext(snapshot, talk))
                throw new InvalidOperationException("Captured native speaker snapshot is not current for its exact Talk binding.");
            if (snapshot.Binding.ActorCategory != expectedCategory || snapshot.Binding.RequestRevision != revision)
                throw new InvalidOperationException("Snapshot category or request revision differs from the live native speaker.");
            if (expectedPersonId == null)
            {
                if (snapshot.Binding.IdentityKind != ConversationIdentityKind.EphemeralSpeaker ||
                    !string.IsNullOrEmpty(snapshot.Binding.PersonId) || string.IsNullOrWhiteSpace(snapshot.Binding.EphemeralSpeakerToken))
                    throw new InvalidOperationException("Unmanaged native actor was persisted as a person instead of an ephemeral speaker.");
            }
            else if (snapshot.Binding.IdentityKind != ConversationIdentityKind.StablePerson ||
                !string.Equals(snapshot.Binding.PersonId, expectedPersonId, StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(snapshot.Binding.EphemeralSpeakerToken))
                throw new InvalidOperationException("Snapshot stable identity differs from the exact native actor.");

            ConversationDialogueRole role;
            bool hasRole = snapshot.Role.TryGetVerified(out role);
            if (expectedRole.HasValue ? (!hasRole || role != expectedRole.Value) : hasRole)
                throw new InvalidOperationException("Snapshot role is not the verified expected role or truthful Unknown.");
            AssertScene(snapshot.PlayerScene, game.PlayerMotor.gameObject.scene.name, "player");
            AssertScene(snapshot.NpcScene, actor is Component ? ((Component)actor).gameObject.scene.name : "", "NPC");
            if (snapshot.Binding.SceneId != ((Component)actor).gameObject.scene.name)
                throw new InvalidOperationException("Snapshot binding scene differs from the exact native speaker scene.");
            if (!snapshot.NpcPosition.TryGetVerified(out ConversationNpcPosition position))
                throw new InvalidOperationException("Snapshot omitted the exact native speaker position.");
            Vector3 live = ((Component)actor).transform.position;
            if (Math.Abs(position.X - live.x) > 0.001 || Math.Abs(position.Y - live.y) > 0.001 || Math.Abs(position.Z - live.z) > 0.001)
                throw new InvalidOperationException("Snapshot NPC position differs from the exact native actor.");
            if (unknownActivity && snapshot.NpcActivity.Status != ConversationContextFactStatus.Unknown)
                throw new InvalidOperationException("Unmanaged/static actor received an invented scheduled activity.");
            AssertNpcOutdoorWeather(snapshot, actor as Component, streets, game);

            return snapshot;
        }

        private static void AssertNpcOutdoorWeather(ConversationContextSnapshot snapshot, Component actor,
            StreetPopulation streets, GameManager game)
        {
            if (!actor) throw new InvalidOperationException("NPC outdoor probe has no exact native actor.");
            Transform npcTransform = actor.transform;
            Scene npcScene = actor.gameObject.scene;
            DaggerfallInterior nativeInterior = npcTransform.GetComponentInParent<DaggerfallInterior>();
            BuildingDirectory directory = game.StreamingWorld != null && game.StreamingWorld.IsReady
                ? game.StreamingWorld.GetCurrentBuildingDirectory() : null;
            bool sameExteriorScene = directory && npcScene.IsValid() && npcScene.isLoaded &&
                directory.gameObject.scene == npcScene;
            ResidentActor residentActor = actor.GetComponent<ResidentActor>();
            bool managedOwner = residentActor == null || streets &&
                residentActor.Resident != null && !string.IsNullOrWhiteSpace(residentActor.Resident.Id) &&
                streets.FindActor(residentActor.Resident.Id) == residentActor;
            bool verifiedExterior = !nativeInterior && managedOwner && sameExteriorScene && directory &&
                (residentActor ? npcTransform.parent == directory.transform : npcTransform.IsChildOf(directory.transform));
            bool verifiedInterior = nativeInterior && (residentActor == null ||
                streets && streets.FindActor(residentActor.Resident.Id) == residentActor &&
                npcTransform.parent == nativeInterior.transform);

            // The managed Gothway outdoor fixture must prove the positive case from
            // its StreetPopulation ownership and exact exterior directory parent.
            if (residentActor && !verifiedExterior)
                throw new InvalidOperationException("Managed outdoor fixture is not owned by the current exterior StreetPopulation directory.");

            bool outdoors;
            if (verifiedExterior)
            {
                if (!snapshot.NpcOutdoors.TryGetVerified(out outdoors) || !outdoors)
                    throw new InvalidOperationException("NPC in the active DaggerfallWorld scene did not receive verified outdoor status.");
            }
            else if (verifiedInterior)
            {
                if (!snapshot.NpcOutdoors.TryGetVerified(out outdoors) || outdoors ||
                    snapshot.Weather.Status != ConversationContextFactStatus.Unknown)
                    throw new InvalidOperationException("NPC in a DaggerfallInterior scene did not receive verified indoor status without weather.");
            }
            else if (snapshot.NpcOutdoors.Status != ConversationContextFactStatus.Unknown ||
                snapshot.Weather.Status != ConversationContextFactStatus.Unknown)
                throw new InvalidOperationException("Unclassified NPC scene received an outdoor or weather assertion.");

            if (snapshot.Weather.Status == ConversationContextFactStatus.Verified)
            {
                if (!verifiedExterior || !snapshot.Weather.TryGetVerified(out ConversationNpcWeatherObservation observation) ||
                    snapshot.Weather.Source != ConversationContextFactSource.NpcSceneObservation ||
                    !string.Equals(observation.SceneId, snapshot.Binding.SceneId, StringComparison.Ordinal) ||
                    observation.SceneRevision != snapshot.Binding.SceneRevision || string.IsNullOrWhiteSpace(observation.Weather))
                    throw new InvalidOperationException("Verified NPC weather was not bound to the exact active outdoor NPC scene and revision.");
            }
            if (verifiedExterior)
            {
                var weatherManager = game.WeatherManager;
                var playerWeather = weatherManager ? weatherManager.PlayerWeather : null;
                string expectedWeather = playerWeather ? WeatherName(playerWeather.WeatherType) : null;
                if (expectedWeather != null &&
                    (!snapshot.Weather.TryGetVerified(out ConversationNpcWeatherObservation expectedObservation) ||
                     !string.Equals(expectedObservation.Weather, expectedWeather, StringComparison.Ordinal)))
                    throw new InvalidOperationException("NPC outdoor weather differs from the current verified outdoor world weather.");
            }
        }

        private static string WeatherName(WeatherType weather)
        {
            switch (weather)
            {
                case WeatherType.Sunny: return "sunny";
                case WeatherType.Cloudy: return "cloudy";
                case WeatherType.Overcast: return "overcast";
                case WeatherType.Fog: return "fog";
                case WeatherType.Rain: return "rain";
                case WeatherType.Thunder: return "thunder";
                case WeatherType.Snow: return "snow";
                default: return null;
            }
        }

        private static void AssertPromptProjection(PeopleOfTheIliacBayMod host, ConversationContextSnapshot snapshot,
            DaggerfallTalkWindow talk, GameManager game, bool unknownActivity)
        {
            string projection = ConversationContextFormatter.ForSocialReply(snapshot, 3072);
            if (string.IsNullOrEmpty(projection) ||
                !projection.Contains("Player scene at submission: " + game.PlayerMotor.gameObject.scene.name + ".") ||
                !projection.Contains("NPC scene: " + snapshot.Binding.SceneId + "."))
                throw new InvalidOperationException("Social prompt projection omitted or conflated the player and NPC scene sources.");
            if (unknownActivity && !projection.Contains("NPC activity: unknown."))
                throw new InvalidOperationException("Social prompt invented an itinerary/activity for a native actor without managed schedule evidence.");

            if (snapshot.PlayerLocationAtSubmission == null)
            {
                if (!projection.Contains("Player appointment HERE at submission: unknown."))
                    throw new InvalidOperationException("Unknown player HERE was replaced by a fabricated appointment place.");
            }
            else if (!projection.Contains("Player appointment HERE at submission: ") ||
                !projection.Contains(" (fixed)."))
                throw new InvalidOperationException("Verified player HERE was not projected as its own fixed submission snapshot.");

            string capturedMemory = host.ReadCapturedConversationMemory(snapshot, talk);
            if (capturedMemory == null || capturedMemory.Length > 4096 ||
                !string.Equals(capturedMemory, host.ReadCapturedConversationMemory(snapshot, talk), StringComparison.Ordinal))
                throw new InvalidOperationException("Bound prompt memory was not a stable snapshot-owned bounded projection.");
        }

        private static IEnumerator EnterNativeGeneralStore(GameManager game)
        {
            if (game.PlayerEnterExit.IsPlayerInsideBuilding)
            {
                if (game.PlayerEnterExit.BuildingType == DFLocation.BuildingTypes.GeneralStore) yield break;
                throw new InvalidOperationException("Speaker probe cannot leave a non-shop interior fixture automatically.");
            }

            BuildingDirectory directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            if (!directory) throw new InvalidOperationException("PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SPEAKER_UNAVAILABLE: current Gothway building directory.");
            DaggerfallStaticDoors owner = null;
            StaticDoor entry = default(StaticDoor);
            foreach (DaggerfallStaticDoors doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                foreach (StaticDoor door in doors.Doors)
                {
                    BuildingSummary summary;
                    if (door.doorType != DoorTypes.Building || !directory.GetBuildingSummary(door.buildingKey, out summary) ||
                        summary.BuildingType != DFLocation.BuildingTypes.GeneralStore) continue;
                    owner = doors;
                    entry = door;
                    break;
                }
                if (owner) break;
            }
            if (!owner) throw new InvalidOperationException("PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SPEAKER_UNAVAILABLE: native GeneralStore door in Gothway Garden.");

            TownSmokeHarness.EnterInterior(owner.transform, entry);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!game.PlayerEnterExit.IsPlayerInsideBuilding && Time.realtimeSinceStartup < deadline) yield return null;
            if (!game.PlayerEnterExit.IsPlayerInsideBuilding ||
                game.PlayerEnterExit.BuildingType != DFLocation.BuildingTypes.GeneralStore)
                throw new InvalidOperationException("Native GeneralStore door did not complete the ordinary interior transition.");
            CloseToHud();
            yield return null;
        }

        private static ResidentActor FindManagedSpeaker(StreetPopulation streets, WorldStateStore store, GameManager game)
        {
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            Scene scene = game.PlayerMotor.gameObject.scene;
            foreach (ResidentActor actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (!actor || !actor.Person || actor.Resident == null || !actor.Resident.IsAlive ||
                    actor.Resident.AgeGroup != ResidentAgeGroup.Adult || actor.IsSleeping || actor.IsConversing ||
                    actor.gameObject.scene != scene || streets.FindActor(actor.Resident.Id) != actor ||
                    store.ConversationTranscript(actor.Resident.Id, now).Count != 0)
                    continue;
                return actor;
            }
            return null;
        }

        private static MobilePersonNPC FindUnmanagedMobile(GameManager game, bool guard)
        {
            Scene scene = game.PlayerMotor.gameObject.scene;
            foreach (GameObject candidate in ActiveGameObjectDatabase.GetActiveCivilianMobileObjects())
            {
                if (!candidate || !candidate.activeInHierarchy || candidate.scene != scene) continue;
                MobilePersonNPC person = candidate.GetComponent<MobilePersonNPC>();
                if (!person || !person.isActiveAndEnabled || person.GetComponent<ResidentActor>() || person.IsGuard != guard)
                    continue;
                return person;
            }

            // DFU's active-civilian cache does not include every native mobile
            // currently loaded by all town/guard population paths. A scene query
            // is a read-only fallback over real live components; it never creates
            // an actor or changes IsGuard/category state.
            foreach (MobilePersonNPC person in UnityEngine.Object.FindObjectsOfType<MobilePersonNPC>())
            {
                if (!person || !person.isActiveAndEnabled || !person.gameObject.activeInHierarchy ||
                    person.gameObject.scene != scene || person.GetComponent<ResidentActor>() || person.IsGuard != guard)
                    continue;
                return person;
            }
            return null;
        }

        private static StaticNPC FindStaticSpeaker(DaggerfallInterior room, GameManager game,
            WorldStateStore store, bool requireKeeper)
        {
            StaticNPC fallback = null;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            foreach (StaticNPC npc in room.GetComponentsInChildren<StaticNPC>(true))
            {
                if (!npc || !npc.gameObject.activeInHierarchy || npc.IsChildNPC ||
                    npc.Data.context != StaticNPC.Context.Building ||
                    npc.Data.mapID != game.PlayerGPS.CurrentLocation.MapTableData.MapId ||
                    npc.GetComponent<QuestResourceBehaviour>() || npc.GetComponent<DaggerfallAction>() ||
                    game.TalkManager.IsNpcOfferingQuest(npc.Data.nameSeed) ||
                    game.TalkManager.IsCastleNpcOfferingQuest(npc.Data.nameSeed))
                    continue;
                bool keeper = NativeBusinessServices.IsKeeper(npc, DFLocation.BuildingTypes.GeneralStore);
                if (keeper != requireKeeper) continue;
                if (store.ConversationTranscript(StaticPersonId(npc), now).Count != 0) continue;
                if (!requireKeeper && npc.Data.billboardArchiveIndex == 184 && npc.Data.billboardRecordIndex == 2)
                    return npc;
                if (fallback == null) fallback = npc;
            }
            return fallback;
        }

        private static DaggerfallTalkWindow BoundTalk(PeopleOfTheIliacBayMod host, GameManager game,
            MobilePersonNPC mobile, StaticNPC stationary)
        {
            DaggerfallTalkWindow talk = DaggerfallUI.UIManager == null ? null : DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            if (talk == null || !talk.IsSetup || !SessionIsBound(host, talk,
                mobile ? (UnityEngine.Object)mobile : stationary, null, false)) return null;
            if (mobile && (game.TalkManager.CurrentNPCType != TalkManager.NPCType.Mobile || game.TalkManager.MobileNPC != mobile)) return null;
            if (stationary && (game.TalkManager.CurrentNPCType != TalkManager.NPCType.Static || game.TalkManager.StaticNPC != stationary)) return null;
            return talk;
        }

        private static bool SessionIsBound(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            UnityEngine.Object speaker, string expectedPersonId, bool comparePersonId = true)
        {
            if (talk == null || DaggerfallUI.UIManager == null || DaggerfallUI.UIManager.TopWindow != talk ||
                !DaggerfallUI.UIManager.ContainsWindow(talk)) return false;
            FieldInfo sessionOpen = typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", InstanceMembers);
            FieldInfo memoryWindow = typeof(PeopleOfTheIliacBayMod).GetField("memoryWindow", InstanceMembers);
            FieldInfo boundSpeaker = typeof(PeopleOfTheIliacBayMod).GetField("conversationContextSessionSpeaker", InstanceMembers);
            FieldInfo personId = typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId", InstanceMembers);
            return sessionOpen != null && memoryWindow != null && boundSpeaker != null && personId != null &&
                (bool)sessionOpen.GetValue(host) && ReferenceEquals(memoryWindow.GetValue(host), talk) &&
                ReferenceEquals(boundSpeaker.GetValue(host), speaker) &&
                (!comparePersonId || string.Equals(personId.GetValue(host) as string, expectedPersonId, StringComparison.Ordinal));
        }

        private static IEnumerator WaitForSessionClose(PeopleOfTheIliacBayMod host)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                FieldInfo sessionOpen = typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", InstanceMembers);
                if (sessionOpen == null || !(bool)sessionOpen.GetValue(host)) yield break;
                yield return null;
            }
            throw new InvalidOperationException("Native Talk session did not close before the next actor fixture.");
        }

        private static void AssertScene(ConversationContextFact<string> fact, string expected, string label)
        {
            string value;
            if (fact == null || !fact.TryGetVerified(out value) ||
                !string.Equals(value, expected, StringComparison.Ordinal))
                throw new InvalidOperationException("Snapshot " + label + " scene is not the exact live native scene.");
        }

        private static string StaticPersonId(StaticNPC npc)
        {
            return "npc:" + npc.Data.mapID.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                npc.Data.buildingKey.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                npc.Data.hash.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void AssertNativeKeeperWorkDuty(ConversationContextSnapshot snapshot, StaticNPC npc, GameManager game)
        {
            if (snapshot == null || npc == null || game == null || snapshot.MotivationContext == null ||
                snapshot.Binding.IdentityKind != ConversationIdentityKind.StablePerson ||
                !string.Equals(snapshot.Binding.PersonId, StaticPersonId(npc), StringComparison.Ordinal) ||
                snapshot.MotivationContext.ObserverId != snapshot.Binding.PersonId ||
                snapshot.MotivationContext.SaveBranchId != snapshot.MotivationBranchId ||
                snapshot.MotivationContext.Revision == long.MaxValue ||
                snapshot.MotivationContext.Revision + 1 != snapshot.Revision ||
                snapshot.MotivationContext.CapturedGameTime != snapshot.CapturedGameTime)
                throw new InvalidOperationException("Keeper motivation snapshot is missing or not bound to the exact native actor, branch, revision, and captured time.");

            BuildingDirectory directory = game.StreamingWorld == null ? null : game.StreamingWorld.GetCurrentBuildingDirectory();
            BuildingSummary summary;
            if (!directory || directory.MapID != npc.Data.mapID ||
                !directory.GetBuildingSummary(npc.Data.buildingKey, out summary) ||
                summary.BuildingType != DFLocation.BuildingTypes.GeneralStore ||
                game.PlayerEnterExit.BuildingType != summary.BuildingType)
                throw new InvalidOperationException("Keeper work duty fixture is not the exact current native GeneralStore site.");

            int type = (int)summary.BuildingType;
            if (type < 0 || type >= PlayerActivate.openHours.Length || type >= PlayerActivate.closeHours.Length)
                throw new InvalidOperationException("Native store hours are unavailable for keeper duty verification.");
            int openHour = PlayerActivate.openHours[type];
            int closeHour = Math.Min(24, (int)PlayerActivate.closeHours[type]);
            if (openHour < 0 || openHour >= closeHour || closeHour > 24)
                throw new InvalidOperationException("Native store hours are invalid for keeper duty verification.");

            double now = snapshot.CapturedGameTime;
            double dayStart = Math.Floor(now / 86400d) * 86400d;
            double expectedStart = dayStart + openHour * 3600d;
            double expectedEnd = dayStart + closeHour * 3600d;
            if (now < expectedStart || now >= expectedEnd)
                throw new InvalidOperationException("Native keeper probe ran outside GeneralStore hours; rerun the probe while the store is open.");

            string siteId = "building:" + npc.Data.mapID.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                npc.Data.buildingKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string expectedId = "work:" + snapshot.Binding.PersonId + ":" + siteId;
            int matches = 0;
            foreach (NpcMotivationDuty duty in snapshot.MotivationContext.Duties)
            {
                if (duty == null || duty.Kind != NpcMotivationDutyKind.Work) continue;
                matches++;
                if (!string.Equals(duty.Id, expectedId, StringComparison.Ordinal) ||
                    duty.Scope != (NpcMotivationDutyScope.RoutineWork | NpcMotivationDutyScope.Schedule) ||
                    duty.Status != NpcMotivationDutyStatus.Active ||
                    duty.KnowledgeState != NpcMotivationKnowledgeState.Known ||
                    duty.StartsAtGameTime != expectedStart || !duty.EndsAtGameTime.HasValue || duty.EndsAtGameTime.Value != expectedEnd ||
                    duty.Revision != snapshot.Binding.RequestRevision || duty.Evidence == null ||
                    duty.Evidence.Kind != NpcMotivationEvidenceKind.NativeObservation ||
                    !string.Equals(duty.Evidence.SaveBranchId, snapshot.MotivationBranchId, StringComparison.Ordinal) ||
                    !string.Equals(duty.Evidence.ObserverId, snapshot.Binding.PersonId, StringComparison.Ordinal) ||
                    !string.Equals(duty.Evidence.EvidenceId, "native-work:" + siteId, StringComparison.Ordinal) ||
                    duty.Evidence.Revision != snapshot.Binding.RequestRevision || duty.Evidence.ObservedGameTime != now)
                    throw new InvalidOperationException("Keeper Work duty did not match the native site, exact DFU open/close hours, request revision, and observation evidence.");
            }
            if (matches != 1)
                throw new InvalidOperationException("Expected exactly one verified Work duty for the native GeneralStore keeper; found " + matches + ".");
        }

        private static void AssertNoKeeperWorkDuty(ConversationContextSnapshot snapshot, string personId)
        {
            if (snapshot == null || snapshot.MotivationContext == null ||
                !string.Equals(snapshot.MotivationContext.ObserverId, personId, StringComparison.Ordinal))
                throw new InvalidOperationException("Same-venue patron motivation snapshot is missing or bound to another actor.");
            foreach (NpcMotivationDuty duty in snapshot.MotivationContext.Duties)
                if (duty != null && duty.Kind == NpcMotivationDutyKind.Work)
                    throw new InvalidOperationException("A same-venue non-keeper patron received a keeper Work duty.");
        }

        private static void AssertWrongStaticSpeakerRejected(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            GameManager game, DaggerfallTalkWindow talk, StaticNPC wrongSpeaker)
        {
            if (!wrongSpeaker) throw new InvalidOperationException("Wrong-speaker check lacks the same-venue native counterpart.");
            try
            {
                CaptureAndAssert(host, streets, game, talk, wrongSpeaker, StaticPersonId(wrongSpeaker),
                    ConversationActorCategory.Static, null, true);
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message.StartsWith("Snapshot capture refused because the native Talk window/session does not own the selected actor.", StringComparison.Ordinal))
                    return;
                throw;
            }
            throw new InvalidOperationException("A snapshot was accepted for a different static speaker while the keeper Talk session was bound.");
        }

        private static HashSet<string> MotivationOwners(WorldStateStore store)
        {
            var owners = new HashSet<string>(StringComparer.Ordinal);
            WorldState state = store == null ? null : store.Snapshot();
            if (state != null && state.NpcMotivations != null)
                foreach (NpcMotivationState row in state.NpcMotivations)
                    if (row != null && !string.IsNullOrWhiteSpace(row.OwnerPersonId)) owners.Add(row.OwnerPersonId);
            return owners;
        }

        private static void AssertMotivationOwnersUnchanged(WorldStateStore store, HashSet<string> before)
        {
            if (before == null || !before.SetEquals(MotivationOwners(store)))
                throw new InvalidOperationException("Read-only conversation snapshot capture added or removed a persisted NPC motivation row.");
        }

        private static WorldStateStore ReadStore(PeopleOfTheIliacBayMod host)
        {
            FieldInfo field = typeof(PeopleOfTheIliacBayMod).GetField("store", InstanceMembers);
            return field == null ? null : field.GetValue(host) as WorldStateStore;
        }

        private static string CommandLineValue(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], key, StringComparison.Ordinal)) return args[i + 1];
            return null;
        }

        private static void CloseToHud()
        {
            if (DaggerfallUI.Instance != null) DaggerfallUI.Instance.PopToHUD();
        }

        private static void RecordUnavailable(List<string> unavailable, string requirement)
        {
            string message = "PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SPEAKER_UNAVAILABLE: disposable Gothway fixture lacks " + requirement + ".";
            Debug.LogError(message);
            unavailable.Add(requirement);
        }
    }
}
