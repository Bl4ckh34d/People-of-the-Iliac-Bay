using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game.Weather;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallConnect.Arena2;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private sealed class ConversationSpeakerCapture
        {
            public UnityEngine.Object NativeObject;
            public Transform Transform;
            public ResidentActor ManagedActor;
            public ConversationActorCategory Category;
            public string PersonId;
            public string Name;
            public string Race;
            public string Gender;
            public ConversationDialogueRole? Role;
            public uint? PersonalitySeed;
        }

        private ConversationContextFact<string> CaptureCurrentJourney(WorldStateStore activeStore,
            ConversationSpeakerCapture speaker,double now,ConversationContextFact<bool> outdoorsFact)
        {
            bool outdoors;
            var game=GameManager.Instance;
            string social;
            if(streets && streets.TryStreetChatContext(speaker.PersonId,now,out social))
                return ConversationContextFact<string>.Verified(social,ConversationContextFactSource.LiveObservation,now);
            if(!speaker.ManagedActor || speaker.ManagedActor.Resident==null || !streets || !streets.Ready ||
                !game.PlayerGPS.HasCurrentLocation || !outdoorsFact.TryGetVerified(out outdoors) ||
                streets.IsAlarmed(speaker.PersonId))return null;
            var resident=speaker.ManagedActor.Resident;
            if(activeStore.ActiveNpcAction(resident.Id)!=null || activeStore.ActiveNpcTask(resident.Id)!=null)
                return null; // Accepted commitments already describe the interruption.
            string plan;
            var medical=activeStore.ReadNpcTreatmentVisit(resident.Id);
            var care=activeStore.ReadNpcHealthCare(resident.Id);
            var cashWait=activeStore.ReadNpcCashWait(resident.Id);
            var cashVisit=activeStore.ReadNpcCashVisit(resident.Id);
            if(medical!=null)
                plan=medical.Returning?"medical visit ended; returning to the ordinary routine":
                    "seeking treatment at "+ConversationBuildingName(medical.ProviderKey)+"; treatment not yet completed";
            else if(care!=null && care.Phase!=NpcHealthCarePhase.Recovered && now>=care.StartedAt)
                plan="ordinary work and visits interrupted for recovery";
            else if(cashWait!=null)
                plan=(speaker.ManagedActor.IsSeated?"waiting at the table for my partner":"waiting here for my partner")+
                    "; my partner is collecting money before joining me; our shared outing is paused, not cancelled";
            else if(cashVisit!=null)
                plan=cashVisit.Returning?(cashVisit.WaitingPartnerId!=null?"returning to my waiting partner after a cash errand":"returning from a cash errand"):
                    cashVisit.WaitingForBank?"I have no spending money with me; waiting for the bank to open so I can collect money before shopping or visiting an Inn":
                    "I have no spending money with me; collecting money "+(cashVisit.Bank?"at the bank ":"at home ")+ConversationBuildingName(cashVisit.ProviderKey)+
                        (cashVisit.WaitingPartnerId!=null?"; my partner is waiting for me":"; ordinary visits will resume afterwards");
            else
            {
                string leisure;
                if(streets.TryClosedShopContext(speaker.ManagedActor,now,ConversationBuildingName,out leisure))
                    return ConversationContextFact<string>.Verified(leisure.Length>384?leisure.Substring(0,384):leisure,ConversationContextFactSource.LiveObservation,now);
                int destination;
                if(outdoors && streets.TryCurrentNavigationBuilding(speaker.ManagedActor,out destination))
                    plan=destination==resident.HomeKey?"returning home; still outdoors":
                        "heading toward "+ConversationBuildingName(destination)+"; still outdoors, not yet inside";
                else
                {
                    var observed=ConversationLifeContext.ObservedActivity(streets.GetState(resident.Id,now),outdoors,
                        game.PlayerEnterExit.IsPlayerInsideBuilding?game.PlayerEnterExit.BuildingDiscoveryData.buildingKey:-1);
                    plan=ManagedConversationPlan(activeStore,resident,now,game.PlayerGPS.CurrentLocation.MapTableData.MapId,observed);
                    int later=plan.IndexOf(';');if(later>=0)plan=plan.Substring(0,later);
                    // A delayed body can lag the timetable. Without an observed
                    // native target, report a plan rather than a verified journey.
                    plan="Scheduled plan (current destination unconfirmed): "+plan;
                }
            }
            if(string.IsNullOrWhiteSpace(plan))return null;
            if(plan.Length>384)plan=plan.Substring(0,384);
            return ConversationContextFact<string>.Verified(plan,ConversationContextFactSource.LiveObservation,now);
        }

        private sealed class CapturedConversationMemory
        {
            public ConversationContextSnapshot Snapshot;
            public DaggerfallTalkWindow Window;
            public string Context;
            public string ArchiveId;
            public string ArchiveHead;
        }

        private readonly Dictionary<int, long> conversationSceneRevisions = new Dictionary<int, long>();
        private int conversationContextMainThreadId;
        private long conversationSaveEpoch;
        private long conversationContextRequestRevision = -1;
        private string conversationContextSessionId;
        private string conversationContextEphemeralSpeakerToken;
        private UnityEngine.Object conversationContextSessionSpeaker;
        private int conversationContextCapturedSceneHandle = int.MinValue;
        private long conversationContextStageSequence;
        private long conversationContextIssuedStageToken;
        private long conversationContextRejectedRequestRevision = -1;
        private ConversationContextSnapshot conversationContextCurrentSnapshot;
        private string conversationContextCapturedPlayerText;
        private CapturedConversationMemory capturedConversationMemory;

        private void RegisterConversationContextLifecycle()
        {
            conversationContextMainThreadId = Thread.CurrentThread.ManagedThreadId;
            SceneManager.sceneLoaded += OnConversationSceneLoaded;
            SceneManager.sceneUnloaded += OnConversationSceneUnloaded;
        }

        private void UnregisterConversationContextLifecycle()
        {
            SceneManager.sceneLoaded -= OnConversationSceneLoaded;
            SceneManager.sceneUnloaded -= OnConversationSceneUnloaded;
        }

        private void OnConversationSceneLoaded(Scene scene, LoadSceneMode mode)
        { CancelDecisionShadow(); EndConversationCommandSession(); EndConversationSpeechSession(); AdvanceConversationSceneRevision(scene.handle); }

        private void OnConversationSceneUnloaded(Scene scene)
        { CancelDecisionShadow(); EndConversationCommandSession(); EndConversationSpeechSession(); AdvanceConversationSceneRevision(scene.handle); }

        private void AdvanceConversationSceneRevision(int sceneHandle)
        {
            EndTrialReplayForConversation(conversationContextSessionId);
            if (breezeTtsTrial) breezeTtsTrial.EndConversation(conversationContextSessionId);
            long revision;
            conversationSceneRevisions.TryGetValue(sceneHandle, out revision);
            if (revision == long.MaxValue) throw new InvalidOperationException("Conversation scene revision exhausted.");
            conversationSceneRevisions[sceneHandle] = revision + 1;
        }

        private long ConversationSceneRevision(int sceneHandle)
        {
            long revision;
            return conversationSceneRevisions.TryGetValue(sceneHandle, out revision) ? revision : 0;
        }

        internal void AdvanceConversationSaveEpoch()
        {
            RequireConversationContextMainThread();
            EndConversationCommandSession();
            EndConversationSpeechSession();
            InvalidatePendingAcceptedSpeechReveal();
            EndTrialReplayForConversation(conversationContextSessionId);
            if (breezeTtsTrial) breezeTtsTrial.EndConversation(conversationContextSessionId);
            CancelDecisionShadow();
            if (conversationSaveEpoch == long.MaxValue)
                throw new InvalidOperationException("Conversation save epoch exhausted.");
            conversationSaveEpoch++;
            ClearSelectedConversationMemoryRead();
            conversationContextIssuedStageToken = 0;
            conversationContextRejectedRequestRevision = -1;
            conversationContextCurrentSnapshot = null;
            conversationContextCapturedPlayerText = null;
            capturedConversationMemory = null;
        }

        private void BeginConversationContextSession(DaggerfallTalkWindow window)
        {
            RequireConversationContextMainThread();
            InvalidatePendingAcceptedSpeechReveal();
            EndTrialReplayForConversation(conversationContextSessionId);
            if (breezeTtsTrial) breezeTtsTrial.EndConversation(conversationContextSessionId);
            CancelDecisionShadow();
            conversationContextSessionId = Guid.NewGuid().ToString("N");
            BeginConversationCommandSession();
            BeginConversationSpeechSession();
            ClearSelectedConversationMemoryRead();
            conversationContextSessionSpeaker = CurrentConversationSpeakerObject();
            conversationContextEphemeralSpeakerToken = null;
            conversationContextRequestRevision = -1;
            conversationContextCapturedSceneHandle = int.MinValue;
            conversationContextIssuedStageToken = 0;
            conversationContextRejectedRequestRevision = -1;
            conversationContextCurrentSnapshot = null;
            conversationContextCapturedPlayerText = null;
            capturedConversationMemory = null;
        }

        private void EndConversationContextSession()
        {
            EndConversationCommandSession();
            EndConversationSpeechSession();
            InvalidatePendingAcceptedSpeechReveal();
            EndTrialReplayForConversation(conversationContextSessionId);
            if (breezeTtsTrial) breezeTtsTrial.EndConversation(conversationContextSessionId);
            CancelDecisionShadow();
            conversationContextSessionId = null;
            ClearSelectedConversationMemoryRead();
            conversationContextSessionSpeaker = null;
            conversationContextEphemeralSpeakerToken = null;
            conversationContextRequestRevision = -1;
            conversationContextCapturedSceneHandle = int.MinValue;
            conversationContextIssuedStageToken = 0;
            conversationContextCurrentSnapshot = null;
            conversationContextCapturedPlayerText = null;
            capturedConversationMemory = null;
            conversationContextRejectedRequestRevision = -1;
        }

        internal ConversationContextSnapshot CaptureConversationContext(
            DaggerfallTalkWindow window, string playerText, long requestRevision)
        {
            RequireConversationContextMainThread();
            if (!IsActiveConversationWindow(window) || string.IsNullOrWhiteSpace(playerText) ||
                requestRevision < 0 || requestRevision <= conversationContextRequestRevision ||
                string.IsNullOrEmpty(conversationContextSessionId) || conversationContextSessionSpeaker == null)
                throw new InvalidOperationException("Conversation context capture requires a new request in the active bound Talk session.");

            ConversationSpeakerCapture speaker;
            if (!TryResolveConversationSpeaker(out speaker) || !ReferenceEquals(speaker.NativeObject, conversationContextSessionSpeaker))
                throw new InvalidOperationException("Current Talk speaker differs from the actor bound to this conversation session.");
            if (!string.Equals(speaker.PersonId, conversationPersonId, StringComparison.Ordinal))
                throw new InvalidOperationException("Current Talk speaker identity differs from the identity used by dialogue memory.");

            string ephemeralToken = speaker.PersonId == null
                ? conversationContextEphemeralSpeakerToken ?? Guid.NewGuid().ToString("N")
                : null;
            if (speaker.PersonId == null && string.IsNullOrEmpty(ephemeralToken))
                throw new InvalidOperationException("Ephemeral speaker identity could not be established.");

            Scene npcScene = speaker.Transform.gameObject.scene;
            Scene playerScene = GameManager.Instance.PlayerMotor.gameObject.scene;
            if (!npcScene.IsValid() || !npcScene.isLoaded || string.IsNullOrWhiteSpace(npcScene.name) ||
                !playerScene.IsValid() || !playerScene.isLoaded || string.IsNullOrWhiteSpace(playerScene.name))
                throw new InvalidOperationException("Conversation actor or player has no loaded native scene.");

            WorldStateStore activeStore = store;
            if (saveFault || activeStore == null)
                throw new InvalidOperationException("Conversation context capture is disabled during save restoration.");
            var nativeClock = DaggerfallUnity.Instance.WorldTime.Now;
            double gameTime = (double)nativeClock.ToSeconds() + (double)nativeClock.Second - Math.Floor(nativeClock.Second);
            long sceneRevision = ConversationSceneRevision(npcScene.handle);

            string clockText = ConversationClock.Context(nativeClock.Hour, nativeClock.Minute,
                nativeClock.DateString(), nativeClock.Year, nativeClock.IsNight);
            ConversationContextFact<string> clock = VerifiedString(clockText,
                ConversationContextFactSource.LiveObservation, gameTime);
            ConversationContextFact<string> name = VerifiedString(speaker.Name,
                ConversationContextFactSource.NativeActor, gameTime);
            ConversationContextFact<ConversationDialogueRole> role = speaker.Role.HasValue
                ? ConversationContextFact<ConversationDialogueRole>.Verified(speaker.Role.Value,
                    ConversationContextFactSource.NativeActor, gameTime)
                : ConversationContextFact<ConversationDialogueRole>.Unknown();
            var playerSceneFact = VerifiedString(playerScene.name, ConversationContextFactSource.LiveObservation, gameTime);
            var npcSceneFact = VerifiedString(npcScene.name, ConversationContextFactSource.LiveObservation, gameTime);
            var position = speaker.Transform.position;
            var npcPosition = ConversationContextFact<ConversationNpcPosition>.Verified(
                new ConversationNpcPosition(position.x, position.y, position.z),
                ConversationContextFactSource.LiveObservation, gameTime);
            ConversationContextFact<ConversationNpcActivity> activity = ObserveNpcActivity(speaker, gameTime);
            ConversationContextFact<bool> npcOutdoors;
            ConversationContextFact<ConversationNpcWeatherObservation> weather;
            CaptureNpcOutdoorWeather(speaker, npcScene, sceneRevision, gameTime, out npcOutdoors, out weather);
            if (reactionToPlayerField == null)
                throw new InvalidOperationException("Native disposition source is unavailable.");
            object nativeDisposition = reactionToPlayerField.GetValue(GameManager.Instance.TalkManager);
            if (!(nativeDisposition is int))
                throw new InvalidOperationException("Native disposition source returned an unsupported value.");
            int disposition = Mathf.Clamp((int)nativeDisposition, -100, 100);
            uint seed = speaker.PersonalitySeed.HasValue ? speaker.PersonalitySeed.Value : 0u;
            var personality = CaptureStablePersonality(activeStore, speaker, gameTime);
            List<ConversationContextCommitment> commitments = CaptureConversationCommitments(activeStore,
                speaker.PersonId, gameTime);
            List<ConversationContextSourceHandle> readSources = CaptureConversationReadSources(activeStore, speaker);
            ConversationContextPersonalState personalState = CaptureConversationPersonalState(activeStore, speaker, gameTime);
            ConversationContextMood persistentMood = speaker.PersonId == null
                ? null
                : CapturePersistentConversationMood(activeStore, speaker.PersonId, gameTime);
            string capturedMemory = CaptureBoundedConversationMemory(playerText);
            // Memory capture can flush the current pending native transcript into
            // this archive. Bind the snapshot to the checkpoint after that sync read.
            ConversationArchiveCheckpointView archive = activeStore.CaptureConversationArchiveCheckpoint();
            var binding = new ConversationContextBinding(speaker.Category, speaker.PersonId, ephemeralToken,
                conversationContextSessionId, requestRevision, conversationSaveEpoch, archive.ArchiveId, archive.Head,
                npcScene.name, sceneRevision);

            ConversationLocationSnapshot playerHere = null;
            AppointmentPlace place;
            if (streets && streets.TryCaptureConversationLocation(out place))
                playerHere = new ConversationLocationSnapshot(gameTime, place);

            var snapshot = new ConversationContextSnapshot(binding, gameTime, clock, name, role,
                playerSceneFact, npcSceneFact, npcPosition, activity, npcOutdoors, weather, personality,
                disposition, seed, playerHere, commitments, readSources, personalState, persistentMood);
            var currentJourney=CaptureCurrentJourney(activeStore,speaker,gameTime,npcOutdoors);
            if(currentJourney!=null) snapshot=snapshot.WithCurrentJourney(currentJourney);
            var sharedOuting = CaptureConversationHouseholdOuting(activeStore, speaker.PersonId, speaker.ManagedActor, gameTime);
            if (sharedOuting != null) snapshot = snapshot.WithHouseholdOuting(sharedOuting);
            if (!string.IsNullOrEmpty(speaker.PersonId))
            {
                var roleEvidence = CaptureConversationRole(activeStore, speaker.PersonId, speaker.NativeObject);
                if (roleEvidence != null) snapshot = snapshot.WithRoleEvidence(roleEvidence);
                var relationship = activeStore.ReadNpcRelationship(speaker.PersonId,
                    NpcDirectedRelationship.PlayerTargetId, gameTime);
                if (relationship != null) snapshot = snapshot.WithDirectedRelationship(relationship);
            }
            // This capture already requires the exact active Talk speaker. It is contact
            // evidence, not a claim that arbitrary outdoor proximity passes stealth checks.
            var acuteFear = string.IsNullOrEmpty(speaker.PersonId) ? null :
                activeStore.ReadNpcAcuteFear(speaker.PersonId, gameTime);
            snapshot = snapshot.WithFearContext(acuteFear, true);
            if (CanAttachMotivationContext(speaker))
            {
                NpcPersonalityProfile savedProfile = activeStore.ReadNpcPersonality(speaker.PersonId);
                NpcMotivationDuty keeperWorkDuty;
                string keeperWorkSiteId;
                TryCaptureNativeKeeperWorkDuty(speaker, snapshot, out keeperWorkDuty, out keeperWorkSiteId);
                NpcMotivationContextSnapshot motivation;
                if (NpcMotivationConversationProjection.TryCapture(snapshot, savedProfile, activeStore,
                    keeperWorkDuty, keeperWorkSiteId, out motivation))
                    snapshot = snapshot.WithMotivationContext(motivation);
            }

            conversationContextEphemeralSpeakerToken = ephemeralToken;
            conversationContextRequestRevision = requestRevision;
            ClearSelectedConversationMemoryRead();
            conversationContextCapturedSceneHandle = npcScene.handle;
            conversationContextIssuedStageToken = 0;
            conversationContextRejectedRequestRevision = -1;
            conversationContextCurrentSnapshot = snapshot;
            conversationContextCapturedPlayerText = playerText;
            capturedConversationMemory = new CapturedConversationMemory
            {
                Snapshot = snapshot,
                Window = window,
                Context = capturedMemory,
                ArchiveId = archive.ArchiveId,
                ArchiveHead = archive.Head
            };
            return snapshot;
        }

        private static ConversationContextMood CapturePersistentConversationMood(WorldStateStore activeStore, string personId,
            double gameTime)
        {
            if (activeStore == null || string.IsNullOrWhiteSpace(personId)) return null;
            var mood = activeStore.CaptureConversationMood(personId, gameTime);
            return mood == null ? null : new ConversationContextMood(mood.Value, mood.AcceptedTurnSequence, mood.Revision);
        }

        private static bool CanAttachMotivationContext(ConversationSpeakerCapture speaker)
        {
            if (speaker == null || string.IsNullOrWhiteSpace(speaker.PersonId) || !speaker.PersonalitySeed.HasValue)
                return false;
            Component component = speaker.NativeObject as Component;
            var stationary = speaker.NativeObject as StaticNPC;
            if (stationary && IsAuthoredNativeIndividual(stationary.Data.factionID)) return false;
            var resident = component ? component.GetComponent<ResidentActor>() : null;
            if (resident && resident.Resident != null && resident.Resident.NativeIdentity != null &&
                IsAuthoredNativeIndividual(resident.Resident.NativeIdentity.FactionId)) return false;
            return true;
        }

        private static ConversationContextFact<string> CaptureStablePersonality(WorldStateStore activeStore,
            ConversationSpeakerCapture speaker, double gameTime)
        {
            RefreshRandomPersonalityEligibility(activeStore, speaker.PersonId, speaker.NativeObject);
            // Do not invent a random persona over authored quest actors.
            if (activeStore == null || string.IsNullOrWhiteSpace(speaker.PersonId) || !speaker.PersonalitySeed.HasValue)
                return ConversationContextFact<string>.Unknown();

            NativePerson native = speaker.ManagedActor && speaker.ManagedActor.Resident != null
                ? speaker.ManagedActor.Resident.NativeIdentity : null;
            var stationary = speaker.NativeObject as StaticNPC;
            int factionId = native != null ? native.FactionId : stationary ? stationary.Data.factionID : -1;
            NpcSocialBackground background;
            if (!TryResolveNpcPersonalityBackground(factionId, out background))
                return ConversationContextFact<string>.Unknown();
            var profile = activeStore.GetOrCreateNpcPersonality(speaker.PersonId, speaker.PersonalitySeed.Value, background);
            return VerifiedString(profile.Description() + " Native social background: " + background + ".",
                ConversationContextFactSource.Derived, gameTime);
        }

        // Create the already-deterministic saved profile before the first
        // native greeting is queued for speech. Reuses the exact speaker seed
        // and background rules used by ordinary conversation snapshots; trial
        // request serialization remains read-only.
        private void EnsureOpeningTrialPersonalityProfile()
        {
            if (!breezeTtsTrial || saveFault || store == null) return;
            ConversationSpeakerCapture speaker;
            if (!TryResolveConversationSpeaker(out speaker) || speaker == null ||
                string.IsNullOrWhiteSpace(speaker.PersonId) ||
                !string.Equals(speaker.PersonId, conversationPersonId, StringComparison.Ordinal) ||
                !string.Equals(speaker.PersonId, memoryPersonId, StringComparison.Ordinal) ||
                !speaker.PersonalitySeed.HasValue) return;

            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            double gameTime = (double)clock.ToSeconds() + (double)clock.Second - Math.Floor(clock.Second);
            CaptureStablePersonality(store, speaker, gameTime);
        }

        internal bool IsCurrentConversationContext(ConversationContextSnapshot snapshot, DaggerfallTalkWindow window)
        {
            if (!IsConversationContextMainThread() || snapshot == null || !ReferenceEquals(snapshot, conversationContextCurrentSnapshot) ||
                snapshot.Binding == null || conversationContextRejectedRequestRevision == snapshot.Binding.RequestRevision ||
                !IsActiveConversationWindow(window) ||
                string.IsNullOrEmpty(conversationContextSessionId) || store == null || saveFault)
                return false;
            if (reactionToPlayerField == null) return false;
            object nativeDisposition = reactionToPlayerField.GetValue(GameManager.Instance.TalkManager);
            if (!(nativeDisposition is int) || Mathf.Clamp((int)nativeDisposition, -100, 100) != snapshot.EffectiveDisposition)
                return false;

            ConversationSpeakerCapture speaker;
            if (!TryResolveConversationSpeaker(out speaker) || !ReferenceEquals(speaker.NativeObject, conversationContextSessionSpeaker))
                return false;
            Scene npcScene = speaker.Transform.gameObject.scene;
            if (!npcScene.IsValid() || !npcScene.isLoaded || npcScene.handle != conversationContextCapturedSceneHandle)
                return false;

            ConversationArchiveCheckpointView archive;
            try { archive = store.CaptureConversationArchiveCheckpoint(); }
            catch { return false; }
            string token = speaker.PersonId == null ? conversationContextEphemeralSpeakerToken : null;
            if (speaker.PersonId == null && string.IsNullOrEmpty(token)) return false;
            var current = new ConversationContextBinding(speaker.Category, speaker.PersonId, token,
                conversationContextSessionId, conversationContextRequestRevision, conversationSaveEpoch,
                archive.ArchiveId, archive.Head, npcScene.name, ConversationSceneRevision(npcScene.handle));
            var currentRelationship = speaker.PersonId == null ? null :
                store.ReadNpcRelationship(speaker.PersonId, NpcDirectedRelationship.PlayerTargetId);
            bool sameRelationship = snapshot.DirectedRelationship == null ? currentRelationship == null :
                currentRelationship != null && snapshot.DirectedRelationship.Revision == currentRelationship.Revision;
            var currentRole = CaptureConversationRole(store, speaker.PersonId, speaker.NativeObject);
            var capturedRole = snapshot.RoleEvidence;
            bool sameRole = SameConversationRole(capturedRole, currentRole);
            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            double now = (double)clock.ToSeconds() + (double)clock.Second - Math.Floor(clock.Second);
            NpcAcuteFearView currentAcuteFear;
            try { currentAcuteFear = speaker.PersonId == null ? null : store.ReadNpcAcuteFear(speaker.PersonId, now); }
            catch (ArgumentOutOfRangeException) { return false; }
            bool sameAcuteFear = snapshot.AcuteFear == null ? currentAcuteFear == null :
                currentAcuteFear != null && snapshot.AcuteFear.Revision == currentAcuteFear.Revision &&
                snapshot.AcuteFear.ObservedGameTime == currentAcuteFear.ObservedGameTime;
            var currentOuting = CaptureConversationHouseholdOuting(store, speaker.PersonId, speaker.ManagedActor, now);
            bool sameOuting = snapshot.HouseholdOuting == null ? currentOuting == null :
                snapshot.HouseholdOuting.HasSameOwnership(currentOuting) &&
                snapshot.HouseholdOuting.PartnerObservedNearby == currentOuting.PartnerObservedNearby &&
                snapshot.HouseholdOuting.ObservedInsideDestination == currentOuting.ObservedInsideDestination;
            return sameRole && sameRelationship && sameAcuteFear && sameOuting && snapshot.Binding.HasSameRequestBinding(current);
        }

        private HouseholdOutingConversation CaptureConversationHouseholdOuting(WorldStateStore activeStore,
            string personId, ResidentActor selectedActor, double gameTime)
        {
            if (activeStore == null || !selectedActor || selectedActor.Resident == null ||
                selectedActor.Resident.Id != personId) return null;
            var outing = activeStore.ReadHouseholdOutingConversation(personId, gameTime);
            if (outing == null) return null;
            var partner = streets ? streets.FindActor(outing.PartnerId) : null;
            var placement = partner ? partner.GetComponent<CrowdMotion>() : null;
            bool nearby = partner && partner.gameObject.activeInHierarchy && partner.Resident != null &&
                partner.Resident.Id == outing.PartnerId && partner.Resident.IsAlive && placement && placement.HasPlacement &&
                partner.gameObject.scene == selectedActor.gameObject.scene &&
                (partner.transform.position - selectedActor.transform.position).sqrMagnitude <= 36f;
            var room = GameManager.Instance.PlayerEnterExit.Interior;
            var gps = GameManager.Instance.PlayerGPS;
            bool destinationTown = gps.HasCurrentLocation && gps.CurrentLocation.MapTableData.MapId == outing.DestinationSettlementId;
            var selectedPlacement = selectedActor.GetComponent<CrowdMotion>();
            bool inside = destinationTown && room && partner && partner.Resident != null && partner.Resident.Id == outing.PartnerId &&
                partner.Resident.IsAlive && placement && placement.HasPlacement && partner.gameObject.activeInHierarchy &&
                selectedPlacement && selectedPlacement.HasPlacement && selectedActor.gameObject.activeInHierarchy &&
                room.EntryDoor.buildingKey == outing.DestinationBuildingKey &&
                selectedActor.transform.IsChildOf(room.transform) && partner.transform.IsChildOf(room.transform);
            return outing.WithObservation(destinationTown ? ConversationBuildingName(outing.DestinationBuildingKey) : null, nearby, inside);
        }

        internal bool InstallConversationContextSuccessor(ConversationContextSnapshot parent,
            ConversationContextSnapshot child, DaggerfallTalkWindow window)
        {
            if (parent == null || child == null || !ReferenceEquals(parent, conversationContextCurrentSnapshot) ||
                !child.IsSuccessorOf(parent) || !IsBoundSnapshotOwnershipCurrent(parent, window) ||
                !ReferenceEquals(child.Binding, parent.Binding) || conversationContextRejectedRequestRevision == parent.Binding.RequestRevision)
                return false;
            ConversationSpeakerCapture speaker;
            if (!TryResolveConversationSpeaker(out speaker)) return false;
            Scene scene = speaker.Transform.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded || scene.handle != conversationContextCapturedSceneHandle ||
                !string.Equals(scene.name, parent.Binding.SceneId, StringComparison.Ordinal) ||
                ConversationSceneRevision(scene.handle) != parent.Binding.SceneRevision) return false;
            if (reactionToPlayerField == null) return false;
            object nativeDisposition = reactionToPlayerField.GetValue(GameManager.Instance.TalkManager);
            if (!(nativeDisposition is int) || Mathf.Clamp((int)nativeDisposition, -100, 100) != child.EffectiveDisposition)
                return false;
            // A disposition change belongs to this same player turn. Preserve
            // its exact command token; do not mint a new ordinal or capability.
            NpcConversationCommandTurn command;
            if (conversationCommandTurns.TryGetValue(parent, out command))
                conversationCommandTurns.Add(child, command);
            conversationContextCurrentSnapshot = child;
            conversationContextIssuedStageToken = 0;
            // A committed disposition successor retains the exact request owner.
            // Preserve its pending rows and delay clock instead of mistaking this
            // authorized snapshot replacement for a speaker/session change.
            if (conversationPendingTracking && ReferenceEquals(conversationPendingSnapshot, parent) &&
                conversationPendingBinding != null &&
                conversationPendingBinding.HasSameRequestBinding(child.Binding))
            {
                conversationPendingSnapshot = child;
                conversationPendingBinding = child.Binding;
            }
            if (capturedConversationMemory != null && ReferenceEquals(capturedConversationMemory.Snapshot, parent))
                capturedConversationMemory.Snapshot = child;
            return true;
        }

        internal bool HandleRejectedConversationStage(ConversationContextSnapshot snapshot,
            DaggerfallTalkWindow window, long stageToken, string originalText, string verifiedFallback = null)
        {
            if (snapshot == null || stageToken < 0 || !IsRejectedConversationRequestOwned(snapshot, window) ||
                conversationContextRejectedRequestRevision == snapshot.Binding.RequestRevision ||
                (stageToken == 0 ? conversationContextIssuedStageToken != 0 : stageToken != conversationContextIssuedStageToken))
                return false;

            conversationContextIssuedStageToken = 0;
            conversationContextRejectedRequestRevision = snapshot.Binding.RequestRevision;
            conversationBusy = false;

            // A modal may cover Talk while its exact request is still pending. Release
            // that request, but only send an NPC line if the original native speaker
            // and archive namespace are still bound. If the actor changed, preserve
            // the player's text as an unanswered presentation line; never speak for
            // or journal the replacement actor.
            if (!IsBoundSnapshotSpeakerCurrent(snapshot))
            {
                AppendRejectedPlayerTextOnly(window, originalText);
                return true;
            }

            string response = string.IsNullOrWhiteSpace(verifiedFallback)
                ? "I lost my train of thought. Please say that again."
                : DialogueReplyFallback.Select(verifiedFallback);
            if (string.IsNullOrWhiteSpace(originalText))
            {
                // Silent returning greetings have no player turn to preserve.
                if (!string.IsNullOrWhiteSpace(verifiedFallback)) AppendReturningGreeting(window, response);
                return true;
            }
            AppendRememberedDialoguePair(window, originalText, response,
                string.IsNullOrWhiteSpace(verifiedFallback) ? "unavailable" : "action");
            return true;
        }

        private bool IsRejectedConversationRequestOwned(ConversationContextSnapshot snapshot, DaggerfallTalkWindow window)
        {
            return IsConversationContextMainThread() && snapshot != null && snapshot.Binding != null &&
                ReferenceEquals(snapshot, conversationContextCurrentSnapshot) && window != null &&
                window == memoryWindow && conversationSessionOpen && DaggerfallUI.UIManager != null &&
                DaggerfallUI.UIManager.ContainsWindow(window) && store != null && !saveFault &&
                snapshot.Binding.RequestRevision == conversationContextRequestRevision &&
                snapshot.Binding.SaveEpoch == conversationSaveEpoch &&
                string.Equals(snapshot.Binding.SessionId, conversationContextSessionId, StringComparison.Ordinal);
        }

        private bool IsBoundSnapshotSpeakerCurrent(ConversationContextSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Binding == null || saveFault || store == null) return false;
            ConversationSpeakerCapture speaker;
            if (!TryResolveConversationSpeaker(out speaker) ||
                !ReferenceEquals(speaker.NativeObject, conversationContextSessionSpeaker) ||
                speaker.Category != snapshot.Binding.ActorCategory ||
                !string.Equals(speaker.PersonId, snapshot.Binding.PersonId, StringComparison.Ordinal))
                return false;
            string token = speaker.PersonId == null ? conversationContextEphemeralSpeakerToken : null;
            if (!string.Equals(token, snapshot.Binding.EphemeralSpeakerToken, StringComparison.Ordinal)) return false;
            Scene scene = speaker.Transform.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded || scene.handle != conversationContextCapturedSceneHandle ||
                !string.Equals(scene.name, snapshot.Binding.SceneId, StringComparison.Ordinal) ||
                ConversationSceneRevision(scene.handle) != snapshot.Binding.SceneRevision) return false;
            try
            {
                ConversationArchiveCheckpointView archive = store.CaptureConversationArchiveCheckpoint();
                return string.Equals(archive.ArchiveId, snapshot.Binding.ArchiveId, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private void AppendRejectedPlayerTextOnly(DaggerfallTalkWindow window, string originalText)
        {
            if (window == null || string.IsNullOrWhiteSpace(originalText)) return;
            RemoveConversationPendingOverlayFor(window);
            var transcript = conversationListField.GetValue(window) as ListBox;
            if (transcript == null) return;
            AppendPresentationLine(transcript, originalText, true);
            transcript.GetItem(transcript.Count - 1).textLabel.Name = "PeopleOfTheIliacBayRejectedPlayerTurn";
            memoryCursor = conversationLastTranscriptCount = transcript.Count;
            transcript.SelectedIndex = transcript.Count - 1;
            RefreshTranscriptScroll(window);
        }

        private bool IsBoundSnapshotOwnershipCurrent(ConversationContextSnapshot snapshot, DaggerfallTalkWindow window)
        {
            if (!IsConversationContextMainThread() || snapshot == null ||
                !ReferenceEquals(snapshot, conversationContextCurrentSnapshot) || !IsActiveConversationWindow(window) ||
                store == null || saveFault || snapshot.Binding == null ||
                snapshot.Binding.RequestRevision != conversationContextRequestRevision ||
                snapshot.Binding.SaveEpoch != conversationSaveEpoch ||
                !string.Equals(snapshot.Binding.SessionId, conversationContextSessionId, StringComparison.Ordinal))
                return false;
            ConversationSpeakerCapture speaker;
            if (!TryResolveConversationSpeaker(out speaker) ||
                !ReferenceEquals(speaker.NativeObject, conversationContextSessionSpeaker) ||
                speaker.Category != snapshot.Binding.ActorCategory ||
                !string.Equals(speaker.PersonId, snapshot.Binding.PersonId, StringComparison.Ordinal))
                return false;
            string token = speaker.PersonId == null ? conversationContextEphemeralSpeakerToken : null;
            if (!string.Equals(token, snapshot.Binding.EphemeralSpeakerToken, StringComparison.Ordinal)) return false;
            try
            {
                ConversationArchiveCheckpointView archive = store.CaptureConversationArchiveCheckpoint();
                return string.Equals(archive.ArchiveId, snapshot.Binding.ArchiveId, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        internal long BeginConversationContextStage(ConversationContextSnapshot snapshot, DaggerfallTalkWindow window)
        {
            if (!IsCurrentConversationContext(snapshot, window) || conversationContextIssuedStageToken != 0 ||
                conversationContextStageSequence == long.MaxValue)
                return 0;
            conversationContextIssuedStageToken = ++conversationContextStageSequence;
            return conversationContextIssuedStageToken;
        }

        internal bool TryConsumeConversationContextStage(
            ConversationContextSnapshot snapshot, DaggerfallTalkWindow window, long stageToken)
        {
            if (stageToken <= 0 || stageToken != conversationContextIssuedStageToken ||
                !IsCurrentConversationContext(snapshot, window))
                return false;
            // Selected inventory/memory/mood reads are buffered. Revalidate their evidence only
            // at publication, not in the per-frame pending-indicator validity predicate.
            if (!IsSelectedConversationInventoryCurrent(snapshot)) return false;
            if (!IsSelectedConversationMemoryCurrent(snapshot)) return false;
            conversationContextIssuedStageToken = 0;
            return true;
        }

        private ConversationContextFact<ConversationNpcActivity> ObserveNpcActivity(
            ConversationSpeakerCapture speaker, double gameTime)
        {
            if (speaker.ManagedActor == null) return ConversationContextFact<ConversationNpcActivity>.Unknown();
            ConversationNpcActivity activity;
            if (speaker.ManagedActor.IsSleeping) activity = ConversationNpcActivity.Sleeping;
            // IsConversing is a live Talk state. It does not assert scheduled work or planned activity.
            else if (speaker.ManagedActor.IsConversing) activity = ConversationNpcActivity.Talking;
            else if (speaker.ManagedActor.IsSeated || speaker.ManagedActor.IsStandingStill) activity = ConversationNpcActivity.Stationary;
            else return ConversationContextFact<ConversationNpcActivity>.Unknown();
            return ConversationContextFact<ConversationNpcActivity>.Verified(activity,
                ConversationContextFactSource.LiveObservation, gameTime);
        }

        private List<ConversationContextCommitment> CaptureConversationCommitments(
            WorldStateStore activeStore, string personId, double gameTime)
        {
            var result = new List<ConversationContextCommitment>(18);
            if (activeStore == null || string.IsNullOrWhiteSpace(personId)) return result;

            NpcActionJob action = activeStore.ActiveNpcAction(personId);
            if (action != null && (!NpcActionExecutor.IsTerminal(action) || action.ReturningToRoutine) &&
                Enum.IsDefined(typeof(NpcActionKind), action.Kind) && Enum.IsDefined(typeof(NpcActionPhase), action.Phase))
                result.Add(new ConversationContextCommitment(action.Id, "action-task",
                    action.Phase.ToString().ToLowerInvariant(), action.Revision, action.Kind,
                    action.AcceptedAt, action.DeadlineAt));

            NpcTaskPlan task = activeStore.ActiveNpcTask(personId);
            if (task != null && Enum.IsDefined(typeof(NpcTaskPhase), task.Phase))
                result.Add(new ConversationContextCommitment(task.Id, "task",
                    task.Phase.ToString().ToLowerInvariant(), task.Revision, null,
                    task.AcceptedAt, task.DeadlineAt));

            // Retain only this speaker's current accepted/waiting appointment,
            // or attended appointment whose known window has not ended.
            foreach (AppointmentRecord appointment in activeStore.AppointmentsFor(personId))
            {
                if (result.Count >= 18) break;
                if (appointment == null || string.IsNullOrWhiteSpace(appointment.Id) || appointment.EndsAt < gameTime ||
                    (appointment.Status != "accepted" && appointment.Status != "waiting" && appointment.Status != "attended"))
                    continue;
                result.Add(new ConversationContextCommitment(appointment.Id, "appointment",
                    appointment.Status, activeStore.AppointmentRevision, null,
                    appointment.StartsAt, appointment.EndsAt));
            }
            return result;
        }

        private List<ConversationContextSourceHandle> CaptureConversationReadSources(
            WorldStateStore activeStore, ConversationSpeakerCapture speaker)
        {
            var result = new List<ConversationContextSourceHandle>(1);
            if (activeStore == null || speaker == null || speaker.ManagedActor == null ||
                string.IsNullOrWhiteSpace(speaker.PersonId)) return result;
            NativeNpcInventoryOwner owner = speaker.ManagedActor.GetComponent<NativeNpcInventoryOwner>();
            NativeNpcInventoryBinding binding = owner ? owner.Binding : null;
            if (binding == null || binding.Items == null || !string.Equals(binding.PersonId, speaker.PersonId, StringComparison.Ordinal))
                return result;
            NpcInventoryState saved = activeStore.ReadNpcInventory(speaker.PersonId);
            if (saved == null || saved.Revision != binding.Revision ||
                !string.Equals(saved.PersonId, binding.PersonId, StringComparison.Ordinal)) return result;
            result.Add(new ConversationContextSourceHandle(binding.PersonId, "npc-inventory", saved.Revision));
            return result;
        }

        private ConversationContextPersonalState CaptureConversationPersonalState(
            WorldStateStore activeStore, ConversationSpeakerCapture speaker, double gameTime)
        {
            var race = VerifiedString(speaker.Race, ConversationContextFactSource.NativeActor, gameTime);
            var gender = VerifiedString(speaker.Gender, ConversationContextFactSource.NativeActor, gameTime);
            var familiar = ConversationContextFact<bool>.Unknown();
            var playerName = ConversationContextFact<string>.Unknown();
            var grieving = ConversationContextFact<bool>.Unknown();
            var alarmed = ConversationContextFact<bool>.Unknown();
            if (activeStore != null && !string.IsNullOrWhiteSpace(speaker.PersonId))
            {
                bool knownFamiliar = activeStore.ConversationEncounters(speaker.PersonId) > 1;
                familiar = ConversationContextFact<bool>.Verified(knownFamiliar,
                    ConversationContextFactSource.Save, gameTime);
            }
            if (speaker.ManagedActor != null && speaker.ManagedActor.Resident != null &&
                activeStore != null && streets != null)
            {
                grieving = ConversationContextFact<bool>.Verified(
                    activeStore.BereavementFor(speaker.ManagedActor.Resident, gameTime) != null,
                    ConversationContextFactSource.Save, gameTime);
                alarmed = ConversationContextFact<bool>.Verified(streets.IsAlarmed(speaker.ManagedActor.Resident.Id),
                    ConversationContextFactSource.LiveObservation, gameTime);
            }
            string bodyContext=NativeConversationBodyContext(speaker.ManagedActor);
            NpcCompanionCastSpec[] knownAffordable,currentCandidates;
            // Capture this mutable native spellbook once into the immutable turn snapshot.
            if(NativeNpcCompanionCast.TryDiscoverKnownAffordable(speaker.ManagedActor==null?null:speaker.ManagedActor.gameObject,
                activeStore,out knownAffordable,out currentCandidates))
                bodyContext+=" "+NpcKnownSpellConversationContext.Format(knownAffordable,currentCandidates);
            // Preserve one exact terminal native outcome in this immutable turn snapshot.
            string terminalAction=activeStore==null?string.Empty:
                ConversationTerminalActionContext.Format(activeStore.LatestNpcAction(speaker.PersonId));
            if(!string.IsNullOrEmpty(terminalAction))bodyContext+=" "+terminalAction;
            return new ConversationContextPersonalState(race, gender, familiar, playerName, grieving, alarmed,
                activeStore == null ? null : activeStore.ReadNpcHouseholdKnowledge(speaker.PersonId),
                VerifiedString(bodyContext,
                    ConversationContextFactSource.NativeActor, gameTime),
                CaptureTravelerIdentityContext(activeStore,speaker.PersonId,gameTime));
        }

        private string CaptureBoundedConversationMemory(string playerText)
        {
            const int maximumCapturedCharacters = 4096;
            string context = CurrentConversationMemory(playerText) ?? string.Empty;
            if (context.Length <= maximumCapturedCharacters) return context;
            // Never trim a quote or memory statement in the middle.
            return " Prior-conversation recall was omitted because it exceeded the safe capture bound. Do not claim a specific remembered detail; ask the player to remind you.";
        }

        internal string ReadCapturedConversationMemory(ConversationContextSnapshot snapshot, DaggerfallTalkWindow window)
        {
            CapturedConversationMemory captured = capturedConversationMemory;
            if (captured == null || !ReferenceEquals(snapshot, captured.Snapshot) || captured.Window != window ||
                !IsCurrentConversationContext(snapshot, window) || snapshot.Binding == null ||
                !string.Equals(captured.ArchiveId, snapshot.Binding.ArchiveId, StringComparison.Ordinal) ||
                !string.Equals(captured.ArchiveHead, snapshot.Binding.ArchiveHead, StringComparison.Ordinal))
                return string.Empty;
            return captured.Context ?? string.Empty;
        }

        private bool TryResolveConversationSpeaker(out ConversationSpeakerCapture capture)
        {
            capture = null;
            var game = GameManager.Instance;
            if (game == null || game.TalkManager == null) return false;
            TalkManager manager = game.TalkManager;
            if (manager.CurrentNPCType == TalkManager.NPCType.Mobile)
            {
                MobilePersonNPC mobile = manager.MobileNPC;
                if (!mobile) return false;
                ResidentActor managed = conversationActor && conversationActor.Person == mobile &&
                    conversationActor.Resident != null && conversationActor.Resident.IsAlive ? conversationActor : null;
                var result = new ConversationSpeakerCapture
                {
                    NativeObject = mobile,
                    Transform = mobile.transform,
                    ManagedActor = managed,
                    Category = managed ? ConversationActorCategory.NativeMobile : mobile.IsGuard
                        ? ConversationActorCategory.Guard : ConversationActorCategory.Mobile,
                    PersonId = managed && !string.IsNullOrWhiteSpace(managed.Resident.Id) ? managed.Resident.Id : null,
                    Name = mobile.NameNPC,
                    Race = mobile.Race.ToString(),
                    Gender = mobile.Gender.ToString(),
                    PersonalitySeed = managed && managed.Resident != null
                        ? (uint?)managed.Resident.AppearanceSeed : null,
                    Role = managed ? (ConversationDialogueRole?)(store!=null && store.Traveler(managed.Resident.Id)!=null
                        ? ConversationDialogueRole.Traveler : ConversationDialogueRole.ManagedResident) :
                        mobile.IsGuard ? (ConversationDialogueRole?)ConversationDialogueRole.Guard : null
                };
                capture = result;
                return true;
            }
            if (manager.CurrentNPCType == TalkManager.NPCType.Static)
            {
                StaticNPC stationary = manager.StaticNPC;
                if (!stationary) return false;
                var data = stationary.Data;
                capture = new ConversationSpeakerCapture
                {
                    NativeObject = stationary,
                    Transform = stationary.transform,
                    Category = ConversationActorCategory.Static,
                    PersonId = StaticConversationPersonId(stationary),
                    Name = stationary.DisplayName,
                    Race = data.race.ToString(),
                    Gender = data.gender.ToString(),
                    Role = CaptureVerifiedStaticRole(stationary),
                    PersonalitySeed = TryNativePersonalitySeed(stationary)
                };
                return true;
            }
            return false;
        }

        private ConversationDialogueRole? CaptureVerifiedStaticRole(StaticNPC npc)
        {
            var game = GameManager.Instance;
            if (!npc || npc.IsChildNPC || game == null) return null;

            bool verifiedShopkeeper = false, verifiedInnkeeper = false;
            if (game.PlayerGPS.HasCurrentLocation &&
                game.PlayerGPS.CurrentLocation.MapTableData.MapId == npc.Data.mapID)
            {
                var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                BuildingSummary summary;
                if (directory && directory.GetBuildingSummary(npc.Data.buildingKey, out summary))
                {
                    var type = summary.BuildingType;
                    if ((type == DaggerfallConnect.DFLocation.BuildingTypes.Tavern || RMBLayout.IsShop(type)) &&
                        NativeBusinessServices.IsKeeper(npc, type))
                    {
                        verifiedInnkeeper = type == DaggerfallConnect.DFLocation.BuildingTypes.Tavern;
                        verifiedShopkeeper = !verifiedInnkeeper;
                    }
                }
            }

            var data = npc.Data;
            FactionFile.FactionData faction = default(FactionFile.FactionData);
            bool factionResolved = game.PlayerEntity != null && game.PlayerEntity.FactionData != null &&
                game.PlayerEntity.FactionData.GetFactionData(data.factionID, out faction);
            bool customFaction = FactionFile.CustomFactions.ContainsKey(data.factionID);
            var facts = new NativeStaticRoleFacts(factionResolved, customFaction, data.factionID,
                factionResolved && faction.type == (int)FactionFile.FactionTypes.Individual,
                factionResolved && faction.sgroup == (int)FactionFile.SocialGroups.Nobility,
                factionResolved && faction.ggroup == (int)FactionFile.GuildGroups.Bards,
                factionResolved ? faction.face : -1, data.billboardArchiveIndex, data.billboardRecordIndex,
                verifiedShopkeeper, verifiedInnkeeper,
                factionResolved && faction.sgroup == (int)FactionFile.SocialGroups.Merchants);
            return StaticNpcRoleProjection.Resolve(facts);
        }

        private uint? TryNativePersonalitySeed(StaticNPC npc)
        {
            if (store == null) return null;
            var data=npc.Data;string expectedId=StaticConversationPersonId(npc);
            if (!string.Equals(conversationPersonId, expectedId, StringComparison.Ordinal)) return null;
            int exteriorTown,exteriorHash;
            bool exterior=NativeExteriorNpcIdentity.TryRead(npc,out exteriorTown,out exteriorHash);
            NativePerson person = store.NativePersonInSettlement(exterior?exteriorTown:data.mapID,expectedId);
            if (person == null || person.NativeHash != (exterior?exteriorHash:data.hash))
                return null;
            // Match the established native-personality seed used by DialogueLife.
            return person.NativeLayoutKey!=null?ConversationNameSeed(person.Id,person.SettlementId):
                StableSeed.Combine(0, (uint)person.SettlementId, (uint)person.WorkKey, unchecked((uint)person.NativeHash));
        }

        private UnityEngine.Object CurrentConversationSpeakerObject()
        {
            var game = GameManager.Instance;
            if (game == null || game.TalkManager == null) return null;
            TalkManager manager = game.TalkManager;
            if (manager.CurrentNPCType == TalkManager.NPCType.Mobile) return manager.MobileNPC;
            if (manager.CurrentNPCType == TalkManager.NPCType.Static) return manager.StaticNPC;
            return null;
        }

        private bool IsActiveConversationWindow(DaggerfallTalkWindow window)
        {
            return IsConversationContextMainThread() && !saveFault && conversationSessionOpen &&
                window != null && window == memoryWindow && !string.IsNullOrEmpty(conversationContextSessionId) &&
                DaggerfallUI.UIManager != null && DaggerfallUI.UIManager.TopWindow == window &&
                DaggerfallUI.UIManager.ContainsWindow(window);
        }

        private bool IsConversationContextMainThread()
        { return conversationContextMainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == conversationContextMainThreadId; }

        private void RequireConversationContextMainThread()
        {
            if (!IsConversationContextMainThread())
                throw new InvalidOperationException("Conversation context lifecycle must run on the Unity main thread.");
        }

        private static ConversationContextFact<string> VerifiedString(string value,
            ConversationContextFactSource source, double gameTime)
        {
            return string.IsNullOrWhiteSpace(value) ? ConversationContextFact<string>.Unknown() :
                ConversationContextFact<string>.Verified(value, source, gameTime);
        }

        private void CaptureNpcOutdoorWeather(ConversationSpeakerCapture speaker, Scene npcScene,
            long sceneRevision, double gameTime,
            out ConversationContextFact<bool> npcOutdoors,
            out ConversationContextFact<ConversationNpcWeatherObservation> weather)
        {
            npcOutdoors = ConversationContextFact<bool>.Unknown();
            weather = ConversationContextFact<ConversationNpcWeatherObservation>.Unknown();
            var game = GameManager.Instance;
            if (speaker == null || !speaker.Transform || !npcScene.IsValid() || !npcScene.isLoaded ||
                speaker.Transform.gameObject.scene != npcScene || game == null || game.StreamingWorld == null ||
                !game.StreamingWorld.IsReady)
                return;

            Transform npc = speaker.Transform;
            DaggerfallInterior npcInterior = npc.GetComponentInParent<DaggerfallInterior>();
            ResidentActor managed = speaker.ManagedActor;
            bool managedOwner = managed == null || streets && managed.Resident != null &&
                !string.IsNullOrWhiteSpace(managed.Resident.Id) &&
                streets.FindActor(managed.Resident.Id) == managed;

            // DFU creates interiors as GameObjects under PlayerEnterExit.InteriorParent,
            // in the loaded Unity world scene. Inspect the NPC's own ancestor hierarchy;
            // scene names and the player's current interior state are not location evidence.
            if (npcInterior)
            {
                if (managedOwner && (managed == null || npc.parent == npcInterior.transform))
                    npcOutdoors = ConversationContextFact<bool>.Verified(false,
                        ConversationContextFactSource.NpcSceneObservation, gameTime);
                return;
            }

            // A town's current BuildingDirectory is a concrete exterior owner. A managed
            // resident must still be registered to StreetPopulation and directly parented
            // by that directory; road-travel/unowned actors remain Unknown.
            BuildingDirectory directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            bool sameDirectoryScene = directory && directory.gameObject.scene == npcScene;
            bool inDirectory = managed == null
                ? directory && npc.IsChildOf(directory.transform)
                : managedOwner && directory && npc.parent == directory.transform;
            if (!sameDirectoryScene || !inDirectory) return;

            npcOutdoors = ConversationContextFact<bool>.Verified(true,
                ConversationContextFactSource.NpcSceneObservation, gameTime);
            // PlayerWeather is DFU's active outdoor-world weather. Attribute it only after
            // the NPC's own transform has been verified inside the current exterior directory.
            var weatherManager = game.WeatherManager;
            var playerWeather = weatherManager ? weatherManager.PlayerWeather : null;
            string observedWeather = playerWeather ? WeatherForConversation(playerWeather.WeatherType) : null;
            if (!string.IsNullOrWhiteSpace(observedWeather))
                weather = ConversationContextFact<ConversationNpcWeatherObservation>.Verified(
                    new ConversationNpcWeatherObservation(observedWeather, npcScene.name, sceneRevision),
                    ConversationContextFactSource.NpcSceneObservation, gameTime);
        }

        private bool TryReadCurrentNpcOutdoorWeather(out bool outdoors, out string weather)
        {
            outdoors = false;
            weather = null;
            var ui = DaggerfallUI.UIManager;
            var window = ui == null ? null : ui.TopWindow as DaggerfallTalkWindow;
            if (window == null || !window.IsSetup || !IsActiveConversationWindow(window) ||
                string.IsNullOrEmpty(conversationContextSessionId) || conversationContextSessionSpeaker == null)
                return false;

            ConversationSpeakerCapture speaker;
            if (!TryResolveConversationSpeaker(out speaker) ||
                !ReferenceEquals(speaker.NativeObject, conversationContextSessionSpeaker))
                return false;

            GameManager game = GameManager.Instance;
            if (game == null || DaggerfallUnity.Instance == null || DaggerfallUnity.Instance.WorldTime == null) return false;
            Scene npcScene = speaker.Transform.gameObject.scene;
            double gameTime = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            long sceneRevision = ConversationSceneRevision(npcScene.handle);
            ConversationContextFact<bool> outdoorsFact;
            ConversationContextFact<ConversationNpcWeatherObservation> weatherFact;
            CaptureNpcOutdoorWeather(speaker, npcScene, sceneRevision, gameTime, out outdoorsFact, out weatherFact);
            if (!outdoorsFact.TryGetVerified(out outdoors)) return false;
            if (outdoors)
            {
                ConversationNpcWeatherObservation observation;
                if (weatherFact.TryGetVerified(out observation) &&
                    string.Equals(observation.SceneId, npcScene.name, StringComparison.Ordinal) &&
                    observation.SceneRevision == sceneRevision)
                    weather = observation.Weather;
            }
            return true;
        }

        private static string WeatherForConversation(WeatherType weather)
        {
            switch (weather)
            {
                case WeatherType.Sunny: return "sunny";
                case WeatherType.Cloudy: return "cloudy";
                case WeatherType.Overcast: return "overcast";
                case WeatherType.Fog: return "fog";
                case WeatherType.Rain:
                    return "rain";
                case WeatherType.Thunder: return "thunder";
                case WeatherType.Snow:
                    return "snow";
                default: return null;
            }
        }

    }
}
