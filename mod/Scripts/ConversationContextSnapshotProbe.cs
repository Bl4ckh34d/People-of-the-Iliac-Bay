using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Dormant native acceptance probe for IMM-02 snapshot binding. It never
    // submits a model request or performs physical actions; stale-recovery checks
    // append one explicit probe exchange to the disposable fixture transcript.
    internal static class ConversationContextSnapshotProbe
    {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            if (!host || !streets || string.IsNullOrEmpty(character) ||
                !NativeProbeText.IsTestCharacter(character))
                throw new InvalidOperationException("Context snapshot probe requires an explicitly owned disposable fixture character.");

            var game = GameManager.Instance;
            if (game == null || !game.IsReady || game.PlayerMotor == null)
                throw new InvalidOperationException("Context snapshot probe requires a ready native game fixture.");

            MethodInfo capture = typeof(PeopleOfTheIliacBayMod).GetMethod("CaptureConversationContext", InstanceMembers);
            MethodInfo current = typeof(PeopleOfTheIliacBayMod).GetMethod("IsCurrentConversationContext", InstanceMembers);
            if (capture == null || current == null)
                throw new InvalidOperationException("IMM-02 runtime capture adapter is not implemented; refusing a fake pass.");
            MethodInfo beginStage = typeof(PeopleOfTheIliacBayMod).GetMethod("BeginConversationContextStage", InstanceMembers);
            MethodInfo consumeStage = typeof(PeopleOfTheIliacBayMod).GetMethod("TryConsumeConversationContextStage", InstanceMembers);
            MethodInfo installSuccessor = typeof(PeopleOfTheIliacBayMod).GetMethod("InstallConversationContextSuccessor", InstanceMembers);
            MethodInfo rejectStage = typeof(PeopleOfTheIliacBayMod).GetMethod("HandleRejectedConversationStage", InstanceMembers);
            MethodInfo readCapturedMemory = typeof(PeopleOfTheIliacBayMod).GetMethod("ReadCapturedConversationMemory", InstanceMembers);
            MethodInfo advanceSaveEpoch = typeof(PeopleOfTheIliacBayMod).GetMethod("AdvanceConversationSaveEpoch", InstanceMembers);
            if (beginStage == null || consumeStage == null || installSuccessor == null || rejectStage == null ||
                readCapturedMemory == null || advanceSaveEpoch == null)
                throw new InvalidOperationException("IMM-02 stage guard or restore-epoch invalidation is not implemented; refusing a fake pass.");

            var storeField = typeof(PeopleOfTheIliacBayMod).GetField("store", InstanceMembers);
            var store = storeField == null ? null : storeField.GetValue(host) as WorldStateStore;
            if (store == null) throw new InvalidOperationException("Context snapshot probe cannot read the restored fixture store.");

            ResidentActor actor = null;
            float actorDeadline = Time.realtimeSinceStartup + 45f;
            while (!actor && Time.realtimeSinceStartup < actorDeadline)
            {
                foreach (var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                {
                    if (!candidate || !candidate.Person || candidate.Resident == null || !candidate.Resident.IsAlive ||
                        candidate.Resident.AgeGroup != ResidentAgeGroup.Adult || candidate.IsSleeping || candidate.IsConversing)
                        continue;
                    // Select an actor with no saved exchange so opening Talk cannot
                    // request an automatic returning greeting.
                    if (store.ConversationTranscript(candidate.Resident.Id,
                        DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).Count != 0) continue;
                    actor = candidate;
                    break;
                }
                // StreetPopulation.Ready precedes its sliced actor spawn work.
                // Wait for the actual native speaker rather than fabricating one.
                if (!actor) yield return new WaitForSecondsRealtime(0.25f);
            }
            if (!actor) throw new InvalidOperationException("No unused managed adult resident is available in the disposable fixture.");

            // Use a detached copy so this case can model repeated encounters without
            // changing the owned save or creating a conversation that introduced a name.
            WorldStateStore originalStore = store;
            WorldState detachedState = store.Snapshot();
            store = new WorldStateStore(detachedState.WorldSeed);
            store.Restore(detachedState);
            double encounterAt = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            store.RecordConversationEncounter(actor.Resident.Id, encounterAt);
            store.RecordConversationEncounter(actor.Resident.Id, encounterAt + 1);
            storeField.SetValue(host, store);

            var manager = game.TalkManager;
            DaggerfallTalkWindow talk = null;
            object latest = null;
            try
            {
                DaggerfallUI.Instance.PopToHUD();
                manager.TalkToMobileNPC(actor.Person);
                float deadline = Time.realtimeSinceStartup + 25f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                    if (talk != null && talk.IsSetup && manager.CurrentNPCType == TalkManager.NPCType.Mobile &&
                        manager.MobileNPC == actor.Person) break;
                    yield return null;
                }
                if (talk == null || !talk.IsSetup || manager.MobileNPC != actor.Person)
                    throw new InvalidOperationException("Native Talk did not bind the selected managed resident.");
                var sessionField = typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", InstanceMembers);
                var beginConversation = typeof(PeopleOfTheIliacBayMod).GetMethod("BeginGroundedConversation", InstanceMembers);
                if (sessionField == null || beginConversation == null)
                    throw new InvalidOperationException("Native dialogue session binding is unavailable.");
                if (!(bool)sessionField.GetValue(host)) beginConversation.Invoke(host, null);
                AssertLifePromptDoesNotInferPlayerName(host, manager, game);

                string playerText = "IMM-02 context capture probe; no dialogue request is sent.";
                object first = capture.Invoke(host, new object[] { talk, playerText, 1L });
                if (first == null) throw new InvalidOperationException("CaptureConversationContext returned null.");
                AssertManagedBinding(first, actor, 1L);
                AssertDetachedSnapshot(first);
                AssertUnknownFactContract(first);
                AssertSceneFacts(first, actor, game);
                AssertPlayerHereSnapshot(first, streets);
                AssertManagedPersonality(first, actor, store);
                AssertCapturedCommitmentsAndInventory(first, store, actor,
                    Convert.ToDouble(Read(first, "CapturedGameTime")));
                AssertPersonalState(first, store, streets, actor, game);
                if (!(bool)current.Invoke(host, new object[] { first, talk }))
                    throw new InvalidOperationException("Fresh snapshot did not match its live Talk binding.");
                string capturedMemory = Convert.ToString(readCapturedMemory.Invoke(host, new object[] { first, talk }));
                if (!string.Equals(capturedMemory,
                    Convert.ToString(readCapturedMemory.Invoke(host, new object[] { first, talk })), StringComparison.Ordinal))
                    throw new InvalidOperationException("Captured memory read changed between accesses.");
                long firstStage = Convert.ToInt64(beginStage.Invoke(host, new object[] { first, talk }));
                if (firstStage <= 0 || !(bool)consumeStage.Invoke(host, new object[] { first, talk, firstStage }) ||
                    (bool)consumeStage.Invoke(host, new object[] { first, talk, firstStage }))
                    throw new InvalidOperationException("Current context stage guard did not allow one call per stage.");
                long nextStage = Convert.ToInt64(beginStage.Invoke(host, new object[] { first, talk }));
                if (nextStage <= firstStage || (bool)consumeStage.Invoke(host, new object[] { first, talk, firstStage }) ||
                    !(bool)consumeStage.Invoke(host, new object[] { first, talk, nextStage }))
                    throw new InvalidOperationException("A later context stage did not invalidate its prior token.");

                // A newer request in the same session supersedes the first.
                object second = capture.Invoke(host, new object[] { talk, playerText, 2L });
                if (second == null) throw new InvalidOperationException("Second CaptureConversationContext returned null.");
                latest = second;
                AssertManagedBinding(second, actor, 2L);
                if ((bool)current.Invoke(host, new object[] { first, talk }))
                    throw new InvalidOperationException("A superseded request binding remained current.");
                if (!(bool)current.Invoke(host, new object[] { second, talk }))
                    throw new InvalidOperationException("Latest request binding was rejected in the same session.");
                if ((bool)consumeStage.Invoke(host, new object[] { first, talk, nextStage }))
                    throw new InvalidOperationException("Stage guard accepted context after newer request capture.");

                AssertDispositionDriftAndSuccessor(host, second, talk, current, installSuccessor);

                object third = capture.Invoke(host, new object[] { talk, playerText, 3L });
                if (third == null || !(bool)current.Invoke(host, new object[] { third, talk }))
                    throw new InvalidOperationException("Fresh request after successor installation did not become current.");
                latest = third;
                long rejectedToken = AssertStaleStageRecovery(host, third, talk, current, beginStage, consumeStage, rejectStage);

                AssertBindingContract(first);
                object fourth = capture.Invoke(host, new object[] { talk, playerText, 4L });
                if (fourth == null || !(bool)current.Invoke(host, new object[] { fourth, talk }))
                    throw new InvalidOperationException("New request did not supersede the recovered stale stage.");
                latest = fourth;
                AssertSpeakerDriftRecovery(host, fourth, talk, actor, store, beginStage, rejectStage);
                latest = null;
                string historyAfterNewer = ReadNativeHistory(talk);
                if ((bool)rejectStage.Invoke(host, new object[] { third, talk, rejectedToken,
                        "IMM-02 rejected-stage probe; no dialogue request is sent.", null }) ||
                    !string.Equals(historyAfterNewer, ReadNativeHistory(talk), StringComparison.Ordinal))
                    throw new InvalidOperationException("Late rejected-stage callback affected a newer request or transcript.");
                long beforeRestoreEpoch = Convert.ToInt64(Read(Read(fourth, "Binding"), "SaveEpoch"));
                advanceSaveEpoch.Invoke(host, null);
                if ((bool)current.Invoke(host, new object[] { fourth, talk }))
                    throw new InvalidOperationException("Save-epoch rotation left a pre-restore request current.");
                object fifth = capture.Invoke(host, new object[] { talk, playerText, 5L });
                if (fifth == null || Convert.ToInt64(Read(Read(fifth, "Binding"), "SaveEpoch")) != beforeRestoreEpoch + 1 ||
                    !(bool)current.Invoke(host, new object[] { fifth, talk }))
                    throw new InvalidOperationException("Request capture did not bind to the rotated save epoch.");
                latest = fifth;
                FieldInfo busyForSessionEnd = typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy", InstanceMembers);
                MethodInfo endMemory = typeof(PeopleOfTheIliacBayMod).GetMethod("EndDialogueMemory", InstanceMembers);
                if (busyForSessionEnd == null || endMemory == null)
                    throw new InvalidOperationException("Dialogue session teardown busy guard is unavailable.");
                busyForSessionEnd.SetValue(host, true);
                endMemory.Invoke(host, new object[] { false });
                if (Convert.ToBoolean(busyForSessionEnd.GetValue(host)) ||
                    (bool)current.Invoke(host, new object[] { fifth, talk }))
                    throw new InvalidOperationException("Ending a pending dialogue session left busy set or its snapshot current.");
                latest = null;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SNAPSHOT_CAPTURE_PASS: exact managed identity, detached facts, separate scenes, fixed nullable HERE, one-shot stages, request supersession, restore-epoch invalidation, and same-branch archive-head binding; no model completion requested.");
            }
            finally
            {
                try
                {
                    if (DaggerfallUI.UIManager != null && DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)
                        DaggerfallUI.Instance.PopToHUD();
                }
                finally { storeField.SetValue(host, originalStore); }
            }
            yield return null;
            if (talk != null && latest != null && (bool)current.Invoke(host, new object[] { latest, talk }))
                throw new InvalidOperationException("Closed Talk session retained a current snapshot binding.");
        }

        private static void AssertManagedBinding(object snapshot, ResidentActor actor, long requestRevision)
        {
            object binding = Read(snapshot, "Binding");
            if (binding == null) throw new InvalidOperationException("Snapshot binding is missing.");
            string category = Convert.ToString(Read(binding, "ActorCategory"));
            if (category != "NativeMobile")
                throw new InvalidOperationException("Managed actor category mismatch: " + category);
            if (Convert.ToString(Read(binding, "IdentityKind")) != "StablePerson")
                throw new InvalidOperationException("Managed resident does not use stable-person identity.");
            if (!string.Equals(Convert.ToString(Read(binding, "PersonId")), actor.Resident.Id, StringComparison.Ordinal))
                throw new InvalidOperationException("Snapshot person ID does not match exact live ResidentActor.");
            if (!string.IsNullOrEmpty(Convert.ToString(Read(binding, "EphemeralSpeakerToken"))))
                throw new InvalidOperationException("Persistent managed speaker was also assigned an ephemeral token.");
            if (Convert.ToInt64(Read(binding, "RequestRevision")) != requestRevision)
                throw new InvalidOperationException("Snapshot request revision differs from the submitted request.");
            RequireNonEmpty(binding, "SessionId");
            RequireNonEmpty(binding, "ArchiveId");
            RequireNonEmpty(binding, "SceneId");
            if (Convert.ToInt64(Read(binding, "SceneRevision")) < 0)
                throw new InvalidOperationException("Snapshot scene revision is invalid.");
            if (Convert.ToInt64(Read(binding, "SaveEpoch")) < 0)
                throw new InvalidOperationException("Snapshot restore epoch is invalid.");
        }

        private static void AssertSceneFacts(object snapshot, ResidentActor actor, GameManager game)
        {
            object playerFact = Read(snapshot, "PlayerScene");
            object npcFact = Read(snapshot, "NpcScene");
            AssertFactShape(Read(snapshot, "ClockContext"), "ClockContext");
            AssertFactShape(Read(snapshot, "Name"), "Name");
            AssertFactShape(Read(snapshot, "Role"), "Role");
            AssertFactShape(playerFact, "PlayerScene");
            AssertFactShape(npcFact, "NpcScene");
            AssertFactShape(Read(snapshot, "NpcPosition"), "NpcPosition");
            AssertFactShape(Read(snapshot, "NpcActivity"), "NpcActivity");

            string playerScene = game.PlayerMotor.gameObject.scene.name;
            AssertFactMatchesScene(playerFact, playerScene, "player");
            // A verified NPC scene must agree with the exact bound actor object.
            // Unknown is valid; the player's scene is never used as a fallback.
            AssertFactMatchesScene(npcFact, actor.gameObject.scene.name, "NPC");

            object npcPositionFact = Read(snapshot, "NpcPosition");
            object npcPositionValue;
            if (IsVerified(npcPositionFact))
            {
                if (!TryGetVerified(npcPositionFact, out npcPositionValue))
                    throw new InvalidOperationException("Verified NPC position did not expose its typed value.");
                Vector3 livePosition = actor.transform.position;
                if (Math.Abs(Convert.ToDouble(Read(npcPositionValue, "X")) - livePosition.x) > 0.001 ||
                    Math.Abs(Convert.ToDouble(Read(npcPositionValue, "Y")) - livePosition.y) > 0.001 ||
                    Math.Abs(Convert.ToDouble(Read(npcPositionValue, "Z")) - livePosition.z) > 0.001)
                    throw new InvalidOperationException("Captured NPC position differs from the exact bound actor's live world position.");
            }

            object npcOutdoors = Read(snapshot, "NpcOutdoors");
            object weather = Read(snapshot, "Weather");
            AssertFactShape(npcOutdoors, "NpcOutdoors");
            AssertFactShape(weather, "Weather");
            object outdoorsValue;
            if (IsVerified(weather) && (!TryGetVerified(npcOutdoors, out outdoorsValue) || !Convert.ToBoolean(outdoorsValue)))
                throw new InvalidOperationException("Snapshot verified weather without verified NPC outdoors state.");
            if (IsVerified(weather))
            {
                object observation;
                if (!TryGetVerified(weather, out observation))
                    throw new InvalidOperationException("Verified weather fact did not expose its typed value.");
                object binding = Read(snapshot, "Binding");
                if (Convert.ToString(Read(weather, "Source")) != "NpcSceneObservation" ||
                    !string.Equals(Convert.ToString(Read(observation, "SceneId")), Convert.ToString(Read(binding, "SceneId")), StringComparison.Ordinal) ||
                    Convert.ToInt64(Read(observation, "SceneRevision")) != Convert.ToInt64(Read(binding, "SceneRevision")))
                    throw new InvalidOperationException("Verified weather is not pinned to the exact current NPC scene revision.");
            }
        }

        private static void AssertPlayerHereSnapshot(object snapshot, StreetPopulation streets)
        {
            object locationFact = Read(snapshot, "PlayerLocationAtSubmission");
            if (!HasMember(snapshot, "PlayerLocationAtSubmission"))
                throw new InvalidOperationException("PlayerLocationAtSubmission field is missing.");
            object expectedPlace = null;
            var method = typeof(StreetPopulation).GetMethod("TryCaptureConversationLocation", InstanceMembers);
            if (method != null)
            {
                object[] args = { null };
                bool captured = (bool)method.Invoke(streets, args);
                if (captured) expectedPlace = args[0];
            }

            object actualPlace = locationFact;
            if (IsFact(locationFact)) TryGetVerified(locationFact, out actualPlace);
            if (actualPlace != null && IsFact(locationFact))
            {
                MethodInfo placeCopy = actualPlace.GetType().GetMethod("PlaceCopy", InstanceMembers);
                if (placeCopy == null) throw new InvalidOperationException("Captured player HERE location has no defensive PlaceCopy API.");
                actualPlace = placeCopy.Invoke(actualPlace, null);
            }
            bool known = IsFact(locationFact) ? IsVerified(locationFact) : actualPlace != null;
            if (expectedPlace != null && !known)
                throw new InvalidOperationException("Snapshot lost a player HERE location that the native source captured.");
            if (expectedPlace == null && known)
                throw new InvalidOperationException("Snapshot claims player HERE when the native source could not capture it.");
            if (expectedPlace != null && actualPlace != null)
            {
                object expectedKind = Read(expectedPlace, "Kind");
                object actualKind = Read(actualPlace, "Kind");
                if (!Equals(expectedKind, actualKind))
                    throw new InvalidOperationException("Snapshot changed player HERE kind after capture.");
                object expectedSettlement = Read(expectedPlace, "SettlementId");
                object actualSettlement = Read(actualPlace, "SettlementId");
                if (!Equals(expectedSettlement, actualSettlement))
                    throw new InvalidOperationException("Snapshot changed player HERE settlement after capture.");
            }
        }

        private static void AssertBindingContract(object snapshot)
        {
            object binding = Read(snapshot, "Binding");
            // Native capture starts at zero, then attaches each present projection
            // once. Require exactly those enrichments rather than accepting any revision.
            long snapshotRevision = Convert.ToInt64(Read(snapshot, "Revision"));
            long expectedCaptureRevision = 1L +
                (Read(snapshot, "DirectedRelationship") == null ? 0L : 1L) +
                (Read(snapshot, "MotivationContext") == null ? 0L : 1L);
            if (snapshotRevision != expectedCaptureRevision)
                throw new InvalidOperationException("Native capture revision does not match its exact fear, relationship, and motivation projections.");
            if (!Convert.ToBoolean(Read(snapshot, "VerifiedPlayerContact")))
                throw new InvalidOperationException("An active native Talk capture did not retain verified player-contact evidence.");
            object acuteFear = Read(snapshot, "AcuteFear");
            if (acuteFear != null &&
                (!string.Equals(Convert.ToString(Read(acuteFear, "ObserverId")),
                    Convert.ToString(Read(binding, "PersonId")), StringComparison.Ordinal) ||
                 Convert.ToDouble(Read(acuteFear, "ProjectedGameTime")) !=
                    Convert.ToDouble(Read(snapshot, "CapturedGameTime"))))
                throw new InvalidOperationException("Native acute fear is not bound to this person and exact capture time.");
            Type type = binding.GetType();
            var ctor = type.GetConstructors(InstanceMembers).FirstOrDefault(candidate => candidate.GetParameters().Length == 10);
            if (ctor == null) throw new InvalidOperationException("ConversationContextBinding 10-field constructor is missing.");
            object[] original = {
                Read(binding, "ActorCategory"), Read(binding, "PersonId"), Read(binding, "EphemeralSpeakerToken"),
                Read(binding, "SessionId"), Convert.ToInt64(Read(binding, "RequestRevision")),
                Convert.ToInt64(Read(binding, "SaveEpoch")), Read(binding, "ArchiveId"), Read(binding, "ArchiveHead"),
                Read(binding, "SceneId"), Convert.ToInt64(Read(binding, "SceneRevision"))
            };
            object sameBranch = ctor.Invoke(new object[] {
                original[0], original[1], original[2], original[3], original[4], original[5], original[6], AdvancedHead(original[7]), original[8], original[9]
            });
            var compare = type.GetMethod("HasSameRequestBinding", InstanceMembers, null, new[] { type }, null);
            if (compare == null) throw new InvalidOperationException("HasSameRequestBinding API is missing.");
            if (!(bool)compare.Invoke(binding, new[] { sameBranch }))
                throw new InvalidOperationException("Same-branch archive-head advancement invalidated request binding.");

            object otherRequest = ctor.Invoke(new object[] {
                original[0], original[1], original[2], original[3], Convert.ToInt64(original[4]) + 1,
                original[5], original[6], original[7], original[8], original[9]
            });
            if ((bool)compare.Invoke(binding, new[] { otherRequest }))
                throw new InvalidOperationException("Different request revision passed binding comparison.");

            object otherEpoch = ctor.Invoke(new object[] {
                original[0], original[1], original[2], original[3], original[4], Convert.ToInt64(original[5]) + 1,
                original[6], original[7], original[8], original[9]
            });
            if ((bool)compare.Invoke(binding, new[] { otherEpoch }))
                throw new InvalidOperationException("Different restore epoch passed binding comparison.");

            object otherSceneRevision = ctor.Invoke(new object[] {
                original[0], original[1], original[2], original[3], original[4], original[5], original[6], original[7],
                original[8], Convert.ToInt64(original[9]) + 1
            });
            if ((bool)compare.Invoke(binding, new[] { otherSceneRevision }))
                throw new InvalidOperationException("Different scene revision passed binding comparison.");

            Type categoryType = original[0].GetType();
            object ephemeralCategory = Enum.Parse(categoryType, "NativeMobile");
            object ephemeral = ctor.Invoke(new object[] {
                ephemeralCategory, null, Guid.NewGuid().ToString("N"), original[3], original[4], original[5], original[6], original[7], original[8], original[9]
            });
            if (!string.IsNullOrEmpty(Convert.ToString(Read(ephemeral, "PersonId"))) ||
                string.IsNullOrEmpty(Convert.ToString(Read(ephemeral, "EphemeralSpeakerToken"))))
                throw new InvalidOperationException("Ephemeral binding did not keep token identity separate from persistent person ID.");
            if ((bool)compare.Invoke(binding, new[] { ephemeral }))
                throw new InvalidOperationException("Ephemeral actor binding matched managed persistent speaker.");

            object staticCategory = Enum.Parse(categoryType, "Static");
            object staticA = ctor.Invoke(new object[] {
                staticCategory, "npc:11:22:33", null, original[3], original[4], original[5], original[6], original[7], original[8], original[9]
            });
            object staticB = ctor.Invoke(new object[] {
                staticCategory, "npc:11:22:34", null, original[3], original[4], original[5], original[6], original[7], original[8], original[9]
            });
            if ((bool)compare.Invoke(staticA, new[] { staticB }))
                throw new InvalidOperationException("Different static native identities passed binding comparison.");

            object guardCategory = Enum.Parse(categoryType, "Guard");
            object guard = ctor.Invoke(new object[] {
                guardCategory, null, Guid.NewGuid().ToString("N"), original[3], original[4], original[5], original[6], original[7], original[8], original[9]
            });
            if (!string.IsNullOrEmpty(Convert.ToString(Read(guard, "PersonId"))) ||
                string.IsNullOrEmpty(Convert.ToString(Read(guard, "EphemeralSpeakerToken"))))
                throw new InvalidOperationException("Ephemeral guard identity was incorrectly made persistent.");
        }

        private static void AssertUnknownFactContract(object snapshot)
        {
            object nameFact = Read(snapshot, "Name");
            MethodInfo unknownFactory = nameFact.GetType().GetMethod("Unknown", StaticMembers, null, Type.EmptyTypes, null);
            if (unknownFactory == null) throw new InvalidOperationException("ConversationContextFact.Unknown factory is missing.");
            object unknown = unknownFactory.Invoke(null, null);
            AssertFactShape(unknown, "synthetic unknown Name");
            if (IsVerified(unknown)) throw new InvalidOperationException("Unknown factory produced verified fact.");
        }

        private static void AssertFactMatchesScene(object fact, string sceneName, string label)
        {
            if (!IsVerified(fact)) return;
            object value;
            if (!TryGetVerified(fact, out value))
                throw new InvalidOperationException("Verified " + label + " fact did not expose its typed value.");
            if (!string.Equals(Convert.ToString(value), sceneName, StringComparison.Ordinal))
                throw new InvalidOperationException("Verified " + label + " scene does not match its own native scene source.");
        }

        private static void AssertManagedPersonality(object snapshot, ResidentActor actor, WorldStateStore store)
        {
            object roleFact = Read(snapshot, "Role");
            object roleValue;
            if (!IsVerified(roleFact) || !TryGetVerified(roleFact, out roleValue) ||
                !string.Equals(Convert.ToString(roleValue), "ManagedResident", StringComparison.Ordinal) ||
                !string.Equals(Convert.ToString(Read(roleFact, "Source")), "NativeActor", StringComparison.Ordinal))
                throw new InvalidOperationException("Exact managed resident role was not captured from the native actor binding.");
            object fact = Read(snapshot, "PersonalityTraits");
            AssertFactShape(fact, "PersonalityTraits");
            if (!IsVerified(fact))
                throw new InvalidOperationException("Known managed resident personality was left Unknown.");
            object value;
            if (!TryGetVerified(fact, out value) ||
                !string.Equals(Convert.ToString(Read(fact, "Source")), "Derived", StringComparison.Ordinal) ||
                store.ReadNpcPersonality(actor.Resident.Id) == null ||
                !string.Equals(Convert.ToString(value), store.ReadNpcPersonality(actor.Resident.Id).Description() +
                    " Native social background: " + store.ReadNpcPersonality(actor.Resident.Id).Background + ".", StringComparison.Ordinal) ||
                Convert.ToUInt32(Read(snapshot, "PersonalitySeed")) != actor.Resident.AppearanceSeed)
                throw new InvalidOperationException("Managed personality fact does not match the saved profile and established AppearanceSeed.");
        }

        private static void AssertCapturedCommitmentsAndInventory(object snapshot, WorldStateStore store,
            ResidentActor actor, double capturedAt)
        {
            var expected = new List<string>();
            NpcActionJob action = store.ActiveNpcAction(actor.Resident.Id);
            if (action != null && (!NpcActionExecutor.IsTerminal(action) || action.ReturningToRoutine) &&
                Enum.IsDefined(typeof(NpcActionKind), action.Kind) && Enum.IsDefined(typeof(NpcActionPhase), action.Phase))
                expected.Add(CommitmentKey(action.Id, "action-task", action.Phase.ToString().ToLowerInvariant(), action.Revision, action.Kind.ToString()));
            NpcTaskPlan task = store.ActiveNpcTask(actor.Resident.Id);
            if (task != null && Enum.IsDefined(typeof(NpcTaskPhase), task.Phase))
                expected.Add(CommitmentKey(task.Id, "task", task.Phase.ToString().ToLowerInvariant(), task.Revision, string.Empty));
            foreach (AppointmentRecord appointment in store.AppointmentsFor(actor.Resident.Id))
            {
                if (expected.Count >= 18) break;
                if (appointment == null || string.IsNullOrWhiteSpace(appointment.Id) || appointment.EndsAt < capturedAt ||
                    (appointment.Status != "accepted" && appointment.Status != "waiting" && appointment.Status != "attended"))
                    continue;
                expected.Add(CommitmentKey(appointment.Id, "appointment", appointment.Status, store.AppointmentRevision, string.Empty));
            }
            expected.Sort(StringComparer.Ordinal);
            var actual = new List<string>();
            var rows = Read(snapshot, "Commitments") as IEnumerable;
            if (rows == null) throw new InvalidOperationException("Snapshot commitments are missing or not enumerable.");
            foreach (object row in rows)
                actual.Add(CommitmentKey(Convert.ToString(Read(row, "Id")), Convert.ToString(Read(row, "Kind")),
                    Convert.ToString(Read(row, "Status")), Convert.ToInt64(Read(row, "Revision")),
                    Convert.ToString(Read(row, "ActionKind"))));
            actual.Sort(StringComparer.Ordinal);
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                throw new InvalidOperationException("Snapshot commitments differ from bounded active action/task/appointment source rows.");

            var expectedSources = new List<string>();
            NativeNpcInventoryOwner owner = actor.GetComponent<NativeNpcInventoryOwner>();
            NativeNpcInventoryBinding inventoryBinding = owner ? owner.Binding : null;
            if (inventoryBinding != null && inventoryBinding.Items != null &&
                string.Equals(inventoryBinding.PersonId, actor.Resident.Id, StringComparison.Ordinal))
            {
                NpcInventoryState ledger = store.ReadNpcInventory(actor.Resident.Id);
                if (ledger != null && ledger.Revision == inventoryBinding.Revision && ledger.PersonId == inventoryBinding.PersonId)
                    expectedSources.Add(inventoryBinding.PersonId + "|npc-inventory|" + ledger.Revision.ToString());
            }
            expectedSources.Sort(StringComparer.Ordinal);
            var actualSources = new List<string>();
            var sources = Read(snapshot, "ReadSources") as IEnumerable;
            if (sources == null) throw new InvalidOperationException("Snapshot read-source handles are missing or not enumerable.");
            foreach (object source in sources)
                actualSources.Add(Convert.ToString(Read(source, "Owner")) + "|" +
                    Convert.ToString(Read(source, "Kind")) + "|" + Convert.ToInt64(Read(source, "Revision")));
            actualSources.Sort(StringComparer.Ordinal);
            if (!expectedSources.SequenceEqual(actualSources, StringComparer.Ordinal))
                throw new InvalidOperationException("Snapshot inventory source handle differs from exact bound owner and ledger revision.");
        }

        private static string CommitmentKey(string id, string kind, string status, long revision, string actionKind)
        { return id + "|" + kind + "|" + status + "|" + revision.ToString() + "|" + (actionKind ?? string.Empty); }

        private static void AssertPersonalState(object snapshot, WorldStateStore store,
            StreetPopulation streets, ResidentActor actor, GameManager game)
        {
            object personal = Read(snapshot, "PersonalState");
            if (personal == null) throw new InvalidOperationException("Snapshot personal-state structure is missing.");
            AssertFactValue(Read(personal, "Race"), actor.Person.Race.ToString(), "NativeActor", "managed race");
            AssertFactValue(Read(personal, "Gender"), actor.Person.Gender.ToString(), "NativeActor", "managed gender");

            bool familiar = store.ConversationEncounters(actor.Resident.Id) > 1;
            if (!familiar)
                throw new InvalidOperationException("Detached context fixture did not establish repeated-encounter familiarity.");
            AssertBooleanFact(Read(personal, "FamiliarWithPlayer"), familiar, "Save", "familiarity");
            object playerName = Read(personal, "KnownPlayerName");
            if (IsVerified(playerName))
                throw new InvalidOperationException("Repeated encounters alone exposed the player's name as a known personal fact.");

            double capturedAt = Convert.ToDouble(Read(snapshot, "CapturedGameTime"));
            AssertBooleanFact(Read(personal, "Grieving"),
                store.BereavementFor(actor.Resident, capturedAt) != null, "Save", "grieving state");
            AssertBooleanFact(Read(personal, "Alarmed"), streets.IsAlarmed(actor.Resident.Id),
                "LiveObservation", "alarm state");
        }

        private static void AssertLifePromptDoesNotInferPlayerName(PeopleOfTheIliacBayMod host,
            TalkManager manager, GameManager game)
        {
            MethodInfo lifeContext = typeof(PeopleOfTheIliacBayMod).GetMethod(
                "CurrentConversationLifeContext", InstanceMembers);
            if (lifeContext == null)
                throw new InvalidOperationException("Conversation life-context prompt is unavailable.");
            string prompt = lifeContext.Invoke(host, new object[] { manager.NameNPC, 0 }) as string;
            if (string.IsNullOrEmpty(prompt) || prompt.Contains(game.PlayerEntity.Name) ||
                !prompt.Contains("Know the player's name only if they state it in this conversation or it appears in your recorded conversation history"))
                throw new InvalidOperationException("Repeated encounters injected the player's native name or omitted the recorded-introduction rule from life context.");
        }

        private static void AssertBooleanFact(object fact, bool expected, string source, string label)
        {
            object value;
            if (!IsVerified(fact) || !TryGetVerified(fact, out value) || Convert.ToBoolean(value) != expected ||
                !string.Equals(Convert.ToString(Read(fact, "Source")), source, StringComparison.Ordinal))
                throw new InvalidOperationException("Captured " + label + " fact differs from its exact source.");
        }

        private static void AssertFactValue(object fact, string expected, string source, string label)
        {
            object value;
            if (string.IsNullOrWhiteSpace(expected) || !IsVerified(fact) || !TryGetVerified(fact, out value) ||
                !string.Equals(Convert.ToString(value), expected, StringComparison.Ordinal) ||
                !string.Equals(Convert.ToString(Read(fact, "Source")), source, StringComparison.Ordinal))
                throw new InvalidOperationException("Captured " + label + " fact differs from its exact source.");
        }

        private static long AssertStaleStageRecovery(PeopleOfTheIliacBayMod host, object snapshot,
            DaggerfallTalkWindow talk, MethodInfo current, MethodInfo beginStage,
            MethodInfo consumeStage, MethodInfo rejectStage)
        {
            const string playerText = "IMM-02 stale stage recovery probe; no dialogue request is sent.";
            long token = Convert.ToInt64(beginStage.Invoke(host, new object[] { snapshot, talk }));
            if (token <= 0) throw new InvalidOperationException("Could not issue the current stale-recovery probe stage token.");
            FieldInfo busyField = typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy", InstanceMembers);
            if (busyField == null) throw new InvalidOperationException("Dialogue busy flag is unavailable for stale recovery assertion.");
            FieldInfo reactionFieldRef = typeof(PeopleOfTheIliacBayMod).GetField("reactionToPlayerField", StaticMembers);
            FieldInfo reactionField = reactionFieldRef == null ? null : reactionFieldRef.GetValue(null) as FieldInfo;
            if (reactionField == null) throw new InvalidOperationException("Native reaction field is unavailable for stale recovery assertion.");
            object manager = GameManager.Instance.TalkManager;
            object priorReaction = reactionField.GetValue(manager);
            if (!(priorReaction is int)) throw new InvalidOperationException("Native reaction value is not an integer.");
            int original = (int)priorReaction;
            int drifted = original < 100 ? original + 1 : original - 1;
            object oldBusy = busyField.GetValue(host);
            var overlay = new DaggerfallMessageBox(DaggerfallUI.UIManager);
            overlay.SetText(new[] { "IMM-02 modal recovery probe", "Talk remains open beneath this message." });
            overlay.ClickAnywhereToClose = true;
            try
            {
                busyField.SetValue(host, true);
                DaggerfallUI.UIManager.PushWindow(overlay);
                if (DaggerfallUI.UIManager.TopWindow == talk || !DaggerfallUI.UIManager.ContainsWindow(talk))
                    throw new InvalidOperationException("Could not cover the still-open Talk window for stale-stage recovery.");
                reactionField.SetValue(manager, drifted);
                if ((bool)current.Invoke(host, new[] { snapshot, (object)talk }))
                    throw new InvalidOperationException("Controlled disposition drift did not stale the current stage.");
                if (!(bool)rejectStage.Invoke(host, new object[] { snapshot, talk, token, playerText, null }))
                    throw new InvalidOperationException("Exact current stale-stage owner was not recovered.");
                if (Convert.ToBoolean(busyField.GetValue(host)))
                    throw new InvalidOperationException("Stale-stage recovery left conversationBusy set.");
                string afterHistory = ReadNativeHistory(talk);
                if (CountExactLine(afterHistory, "Player: " + playerText) != 1 ||
                    CountExactLine(afterHistory, DialogueHistoryWindow.UnavailableResponseLabel +
                        "I lost my train of thought. Please say that again.") != 1 ||
                    CountExactLine(afterHistory, "NPC: I lost my train of thought. Please say that again.") != 0)
                    throw new InvalidOperationException("Stale-stage recovery did not preserve one exact user line and truthful fallback.");
                if ((bool)rejectStage.Invoke(host, new object[] { snapshot, talk, token, playerText, null }) ||
                    (bool)consumeStage.Invoke(host, new object[] { snapshot, talk, token }) ||
                    !string.Equals(afterHistory, ReadNativeHistory(talk), StringComparison.Ordinal))
                    throw new InvalidOperationException("Duplicate callback replayed fallback or consumed a rejected token.");
                if ((bool)current.Invoke(host, new[] { snapshot, (object)talk }))
                    throw new InvalidOperationException("Aborted request remained current after one-shot fallback.");
            }
            finally
            {
                if (DaggerfallUI.UIManager != null && DaggerfallUI.UIManager.TopWindow == overlay)
                    DaggerfallUI.UIManager.PopWindow();
                reactionField.SetValue(manager, priorReaction);
                busyField.SetValue(host, oldBusy);
            }
            if ((int)reactionField.GetValue(manager) != original)
                throw new InvalidOperationException("Stale-stage recovery probe did not restore native reaction.");
            return token;
        }

        private static void AssertSpeakerDriftRecovery(PeopleOfTheIliacBayMod host, object snapshot,
            DaggerfallTalkWindow talk, ResidentActor actor, WorldStateStore store,
            MethodInfo beginStage, MethodInfo rejectStage)
        {
            const string playerText = "IMM-02 changed-speaker recovery probe; do not send this to another actor.";
            long token = Convert.ToInt64(beginStage.Invoke(host, new object[] { snapshot, talk }));
            if (token <= 0) throw new InvalidOperationException("Could not issue the changed-speaker recovery stage token.");
            FieldInfo busyField = typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy", InstanceMembers);
            FieldInfo speakerField = typeof(PeopleOfTheIliacBayMod).GetField("conversationContextSessionSpeaker", InstanceMembers);
            if (busyField == null || speakerField == null)
                throw new InvalidOperationException("Changed-speaker recovery ownership fields are unavailable.");
            object originalSpeaker = speakerField.GetValue(host);
            object originalBusy = busyField.GetValue(host);
            string historyBefore = ReadNativeHistory(talk);
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            int exchangesBefore = store.ConversationTranscript(actor.Resident.Id, now).Count;
            try
            {
                busyField.SetValue(host, true);
                // Simulate a bound-speaker change while keeping the exact Talk
                // request/session alive. Cleanup must preserve the player turn but
                // must not emit or persist a reply under the replacement binding.
                speakerField.SetValue(host, actor.gameObject);
                if (!(bool)rejectStage.Invoke(host, new object[] { snapshot, talk, token, playerText, null }))
                    throw new InvalidOperationException("Current request was not released after speaker binding drift.");
                if (Convert.ToBoolean(busyField.GetValue(host)))
                    throw new InvalidOperationException("Changed-speaker recovery left conversationBusy set.");
                string historyAfter = ReadNativeHistory(talk);
                if (CountExactLine(historyAfter, "Player: " + playerText) -
                        CountExactLine(historyBefore, "Player: " + playerText) != 1 ||
                    CountExactLine(historyAfter, "NPC: I lost my train of thought. Please say that again.") !=
                        CountExactLine(historyBefore, "NPC: I lost my train of thought. Please say that again.") ||
                    !historyAfter.StartsWith(historyBefore, StringComparison.Ordinal))
                    throw new InvalidOperationException("Changed-speaker recovery did not preserve only the original player turn.");
                if (store.ConversationTranscript(actor.Resident.Id, now).Count != exchangesBefore)
                    throw new InvalidOperationException("Changed-speaker recovery journaled text under the original or replacement actor.");
                if ((bool)rejectStage.Invoke(host, new object[] { snapshot, talk, token, playerText, null }) ||
                    !string.Equals(historyAfter, ReadNativeHistory(talk), StringComparison.Ordinal))
                    throw new InvalidOperationException("Duplicate changed-speaker callback replayed the player turn.");
            }
            finally
            {
                speakerField.SetValue(host, originalSpeaker);
                busyField.SetValue(host, originalBusy);
            }
        }

        private static string ReadNativeHistory(DaggerfallTalkWindow talk)
        {
            MethodInfo method = typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory", StaticMembers);
            if (method == null) throw new InvalidOperationException("Native transcript read helper is unavailable.");
            return Convert.ToString(method.Invoke(null, new object[] { talk })) ?? string.Empty;
        }

        private static int CountExactLine(string text, string line)
        {
            int count = 0, index = 0;
            while ((index = text.IndexOf(line, index, StringComparison.Ordinal)) >= 0)
            { count++; index += line.Length; }
            return count;
        }

        private static void AssertDispositionDriftAndSuccessor(
            PeopleOfTheIliacBayMod host, object snapshot, DaggerfallTalkWindow talk, MethodInfo current, MethodInfo installSuccessor)
        {
            FieldInfo dispositionField = typeof(PeopleOfTheIliacBayMod).GetField("reactionToPlayerField", StaticMembers);
            if (dispositionField == null) throw new InvalidOperationException("Native disposition field binding is unavailable.");
            FieldInfo nativeReaction = dispositionField.GetValue(null) as FieldInfo;
            if (nativeReaction == null) throw new InvalidOperationException("Native reaction field is unavailable.");
            object manager = GameManager.Instance.TalkManager;
            object originalValue = nativeReaction.GetValue(manager);
            if (!(originalValue is int)) throw new InvalidOperationException("Native reaction value is not an integer.");
            int original = (int)originalValue;
            int changed = original < 100 ? original + 1 : original - 1;
            object child = null;
            try
            {
                nativeReaction.SetValue(manager, changed);
                if ((bool)current.Invoke(host, new[] { snapshot, (object)talk }))
                    throw new InvalidOperationException("Disposition drift left the parent snapshot current.");

                Type snapshotType = snapshot.GetType();
                MethodInfo withDisposition = snapshotType.GetMethod("WithCommittedDisposition", InstanceMembers);
                MethodInfo isSuccessor = snapshotType.GetMethod("IsSuccessorOf", InstanceMembers, null, new[] { snapshotType }, null);
                if (withDisposition == null || isSuccessor == null)
                    throw new InvalidOperationException("Immutable disposition-successor APIs are unavailable.");
                child = withDisposition.Invoke(snapshot, new object[] { changed });
                if (!(bool)isSuccessor.Invoke(child, new[] { snapshot }))
                    throw new InvalidOperationException("Committed disposition snapshot is not an exact successor of its parent.");
                if (!(bool)installSuccessor.Invoke(host, new[] { snapshot, child, (object)talk }))
                    throw new InvalidOperationException("Runtime rejected the exact current-parent disposition successor.");
                if (!(bool)current.Invoke(host, new[] { child, (object)talk }))
                    throw new InvalidOperationException("Exact committed successor did not match live disposition.");

                int siblingDisposition = original <= 98 ? original + 2 : original - 2;
                object sibling = withDisposition.Invoke(snapshot, new object[] { siblingDisposition });
                if ((bool)isSuccessor.Invoke(child, new[] { sibling }))
                    throw new InvalidOperationException("Committed successor accepted a sibling snapshot as its exact parent.");
                if ((bool)installSuccessor.Invoke(host, new[] { snapshot, sibling, (object)talk }) ||
                    !(bool)current.Invoke(host, new[] { child, (object)talk }))
                    throw new InvalidOperationException("Runtime installed a sibling successor or displaced the accepted child.");
            }
            finally
            {
                nativeReaction.SetValue(manager, originalValue);
            }
            if ((int)nativeReaction.GetValue(manager) != original || (bool)current.Invoke(host, new[] { child, (object)talk }))
                throw new InvalidOperationException("Restoring native reaction failed or left the temporary child current.");
        }

        private static bool TryGetVerified(object fact, out object value)
        {
            value = null;
            if (fact == null) return false;
            MethodInfo method = fact.GetType().GetMethods(InstanceMembers).FirstOrDefault(candidate =>
            {
                if (candidate.Name != "TryGetVerified") return false;
                ParameterInfo[] parameters = candidate.GetParameters();
                return parameters.Length == 1 && parameters[0].IsOut;
            });
            if (method == null) return false;
            object[] args = { null };
            bool verified = (bool)method.Invoke(fact, args);
            value = args[0];
            return verified;
        }

        private static void AssertFactShape(object fact, string field)
        {
            if (fact == null || Read(fact, "Status") == null)
                throw new InvalidOperationException("Snapshot fact/status missing for " + field + ".");
            string status = Convert.ToString(Read(fact, "Status"));
            if (status != "Unknown" && status != "Verified")
                throw new InvalidOperationException("Unsupported fact status for " + field + ": " + status);
            if (status == "Unknown")
            {
                MethodInfo tryGet = fact.GetType().GetMethod("TryGetVerified", InstanceMembers);
                if (tryGet != null)
                {
                    var parameters = tryGet.GetParameters();
                    if (parameters.Length == 1 && parameters[0].IsOut)
                    {
                        object initial = parameters[0].ParameterType.GetElementType().IsValueType
                            ? Activator.CreateInstance(parameters[0].ParameterType.GetElementType()) : null;
                        if ((bool)tryGet.Invoke(fact, new[] { initial }))
                            throw new InvalidOperationException("Unknown fact was exposed as a verified value: " + field);
                    }
                }
            }
        }

        private static bool IsFact(object value)
        { return value != null && value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition().Name.Contains("Fact"); }

        private static bool IsVerified(object fact)
        { return string.Equals(Convert.ToString(Read(fact, "Status")), "Verified", StringComparison.Ordinal); }

        private static void AssertDetachedSnapshot(object snapshot)
        {
            var visited = new HashSet<object>(ReferenceComparer.Instance);
            var pending = new Stack<object>();
            pending.Push(snapshot);
            int inspected = 0;
            while (pending.Count > 0)
            {
                object value = pending.Pop();
                if (value == null || value is string || value.GetType().IsPrimitive || value.GetType().IsEnum ||
                    value is decimal || value is Guid || value is DateTime) continue;
                if (!visited.Add(value)) continue;
                if (++inspected > 4096) throw new InvalidOperationException("Snapshot object graph exceeded inspection bound.");
                Type type = value.GetType();
                string ns = type.Namespace ?? string.Empty;
                if (value is UnityEngine.Object || ns.StartsWith("DaggerfallWorkshop", StringComparison.Ordinal) ||
                    ns.StartsWith("UnityEngine", StringComparison.Ordinal) || value is Delegate)
                    throw new InvalidOperationException("Snapshot retains native/runtime object: " + type.FullName);
                var enumerable = value as IEnumerable;
                if (enumerable != null)
                {
                    foreach (object item in enumerable) if (item != null) pending.Push(item);
                    continue;
                }
                foreach (var field in type.GetFields(InstanceMembers))
                {
                    if (field.IsStatic || field.FieldType.IsPointer) continue;
                    object child;
                    try { child = field.GetValue(value); }
                    catch { continue; }
                    if (child != null) pending.Push(child);
                }
            }
        }

        private static void RequireNonEmpty(object target, string member)
        {
            if (string.IsNullOrEmpty(Convert.ToString(Read(target, member))))
                throw new InvalidOperationException("Snapshot binding member is empty: " + member);
        }

        private static string AdvancedHead(object head)
        {
            string value = Convert.ToString(head);
            if (string.IsNullOrEmpty(value)) return new string('1', 64);
            char first = value[0] == '0' ? '1' : '0';
            return first + value.Substring(1);
        }

        private static object Read(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            var property = type.GetProperty(name, InstanceMembers);
            if (property != null) return property.GetValue(target, null);
            var field = type.GetField(name, InstanceMembers);
            return field == null ? null : field.GetValue(target);
        }

        private static bool HasMember(object target, string name)
        {
            if (target == null) return false;
            Type type = target.GetType();
            return type.GetProperty(name, InstanceMembers) != null || type.GetField(name, InstanceMembers) != null;
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) { return ReferenceEquals(x, y); }
            public int GetHashCode(object value) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value); }
        }
    }
}
