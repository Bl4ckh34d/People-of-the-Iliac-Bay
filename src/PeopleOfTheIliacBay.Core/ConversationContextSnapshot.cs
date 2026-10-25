using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace PeopleOfTheIliacBay.Core
{
    public enum ConversationActorCategory { Mobile, Static, Guard, NativeMobile }
    public enum ConversationIdentityKind { StablePerson, EphemeralSpeaker }
    public enum ConversationContextFactStatus { Unknown, Verified }
    public enum ConversationContextFactSource { Save, NativeActor, LiveObservation, NpcSceneObservation, Derived, CommittedEffect, NotObserved }
    public enum ConversationDialogueRole { ManagedResident, Guard, Shopkeeper, Innkeeper, Traveler, Unknown, Jester, BardGuildMember, Royalty, Merchant }
    public enum ConversationNpcActivity { Stationary, Walking, Working, Sleeping, Talking, Unknown }
    public enum ConversationTextSpeaker { Npc, Player }

    public sealed class ConversationContextBinding
    {
        public ConversationActorCategory ActorCategory { get; }
        public ConversationIdentityKind IdentityKind { get; }
        public string PersonId { get; }
        public string EphemeralSpeakerToken { get; }
        public string SessionId { get; }
        public long RequestRevision { get; }
        public long SaveEpoch { get; }
        public string ArchiveId { get; }
        public string ArchiveHead { get; }
        public string SceneId { get; }
        public long SceneRevision { get; }

        public ConversationContextBinding(ConversationActorCategory actorCategory, string personId,
            string ephemeralSpeakerToken, string sessionId, long requestRevision, long saveEpoch,
            string archiveId, string archiveHead, string sceneId, long sceneRevision)
        {
            if (!Enum.IsDefined(typeof(ConversationActorCategory), actorCategory) ||
                !ValidOptionalId(personId) || !ValidOptionalId(ephemeralSpeakerToken) ||
                (string.IsNullOrWhiteSpace(personId) == string.IsNullOrWhiteSpace(ephemeralSpeakerToken)) ||
                string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 128 || requestRevision < 0 || saveEpoch < 0 ||
                !ConversationArchiveCheckpoint.Hex(archiveId, 32) ||
                (archiveHead != "" && !ConversationArchiveCheckpoint.Hex(archiveHead, 64)) ||
                string.IsNullOrWhiteSpace(sceneId) || sceneId.Length > 256 || sceneRevision < 0)
                throw new ArgumentException("Invalid conversation context binding.");
            ActorCategory = actorCategory;
            IdentityKind = string.IsNullOrWhiteSpace(personId) ? ConversationIdentityKind.EphemeralSpeaker : ConversationIdentityKind.StablePerson;
            PersonId = personId;
            EphemeralSpeakerToken = ephemeralSpeakerToken;
            SessionId = sessionId;
            RequestRevision = requestRevision;
            SaveEpoch = saveEpoch;
            ArchiveId = archiveId;
            ArchiveHead = archiveHead;
            SceneId = sceneId;
            SceneRevision = sceneRevision;
        }

        public bool HasSameRequestBinding(ConversationContextBinding other)
        {
            return other != null && ActorCategory == other.ActorCategory && IdentityKind == other.IdentityKind &&
                string.Equals(PersonId, other.PersonId, StringComparison.Ordinal) &&
                string.Equals(EphemeralSpeakerToken, other.EphemeralSpeakerToken, StringComparison.Ordinal) &&
                string.Equals(SessionId, other.SessionId, StringComparison.Ordinal) &&
                RequestRevision == other.RequestRevision && SaveEpoch == other.SaveEpoch &&
                string.Equals(ArchiveId, other.ArchiveId, StringComparison.Ordinal) &&
                string.Equals(SceneId, other.SceneId, StringComparison.Ordinal) && SceneRevision == other.SceneRevision;
        }

        private static bool ValidOptionalId(string value) => value == null || (value.Length > 0 && value.Length <= 128 && !string.IsNullOrWhiteSpace(value));
    }

    public sealed class ConversationContextFact<T>
    {
        private readonly T value;
        public ConversationContextFactStatus Status { get; }
        public ConversationContextFactSource Source { get; }
        public double ObservedAt { get; }

        private ConversationContextFact(ConversationContextFactStatus status, T factValue,
            ConversationContextFactSource source, double observedAt)
        { Status = status; value = factValue; Source = source; ObservedAt = observedAt; }

        public static ConversationContextFact<T> Unknown() =>
            new ConversationContextFact<T>(ConversationContextFactStatus.Unknown, default(T), ConversationContextFactSource.NotObserved, 0);

        public static ConversationContextFact<T> Verified(T value, ConversationContextFactSource source, double observedAt)
        {
            if (!IsImmutableFactType(typeof(T)) || value == null || !Enum.IsDefined(typeof(ConversationContextFactSource), source) ||
                (typeof(T) == typeof(string) && (string.IsNullOrWhiteSpace((string)(object)value) || ((string)(object)value).Length > 512)) ||
                (typeof(T).IsEnum && !Enum.IsDefined(typeof(T), value)) ||
                (typeof(T).IsEnum && string.Equals(Enum.GetName(typeof(T), value), "Unknown", StringComparison.Ordinal)) ||
                (typeof(T) == typeof(ConversationNpcWeatherObservation) && source != ConversationContextFactSource.NpcSceneObservation) ||
                source == ConversationContextFactSource.NotObserved || double.IsNaN(observedAt) || double.IsInfinity(observedAt) || observedAt < 0)
                throw new ArgumentException("A verified conversation fact must be an immutable value with a valid source and time.");
            return new ConversationContextFact<T>(ConversationContextFactStatus.Verified, value, source, observedAt);
        }

        public bool TryGetVerified(out T verifiedValue)
        {
            verifiedValue = value;
            return Status == ConversationContextFactStatus.Verified;
        }

        internal T ValueOrDefault => value;

        internal static bool IsImmutableFactType(Type type)
        {
            return type == typeof(string) || type.IsEnum || type.IsPrimitive || type == typeof(decimal) ||
                type == typeof(DateTime) || type == typeof(Guid) || type == typeof(ConversationNpcPosition) ||
                type == typeof(ConversationNpcWeatherObservation);
        }
    }

    public readonly struct ConversationNpcPosition
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public ConversationNpcPosition(double x, double y, double z)
        {
            if (!Finite(x) || !Finite(y) || !Finite(z) || Math.Abs(x) > 1000000 || Math.Abs(y) > 1000000 || Math.Abs(z) > 1000000)
                throw new ArgumentException("Invalid observed NPC position.");
            X = x; Y = y; Z = z;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public readonly struct ConversationNpcWeatherObservation
    {
        public string Weather { get; }
        public string SceneId { get; }
        public long SceneRevision { get; }
        public ConversationNpcWeatherObservation(string weather, string sceneId, long sceneRevision)
        {
            if (!ConversationContextCommitment.ValidText(weather, 64) || !ConversationContextCommitment.ValidText(sceneId, 256) || sceneRevision < 0)
                throw new ArgumentException("Invalid NPC-scene weather observation.");
            Weather = weather; SceneId = sceneId; SceneRevision = sceneRevision;
        }
    }

    public sealed class ConversationContextCommitment
    {
        public string Id { get; }
        public string Kind { get; }
        public string Status { get; }
        public long Revision { get; }
        public NpcActionKind? ActionKind { get; }
        public double? StartsAtGameTime { get; }
        public double? EndsAtGameTime { get; }
        public ConversationContextCommitment(string id, string kind, string status, long revision)
            : this(id, kind, status, revision, null, null, null)
        { }

        public ConversationContextCommitment(string id, string kind, string status, long revision, NpcActionKind? actionKind)
            : this(id, kind, status, revision, actionKind, null, null)
        { }

        public ConversationContextCommitment(string id, string kind, string status, long revision,
            NpcActionKind? actionKind, double? startsAtGameTime, double? endsAtGameTime)
        {
            if (!ValidText(id, 128) || !ValidText(kind, 64) || !ValidText(status, 64) || revision < 0)
                throw new ArgumentException("Invalid conversation commitment.");
            if (actionKind.HasValue && (!Enum.IsDefined(typeof(NpcActionKind), actionKind.Value) ||
                !string.Equals(kind, "action-task", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Only action-task commitments may carry a defined action kind.", nameof(actionKind));
            if (startsAtGameTime.HasValue && (!Finite(startsAtGameTime.Value) || startsAtGameTime.Value < 0) ||
                endsAtGameTime.HasValue && (!Finite(endsAtGameTime.Value) || endsAtGameTime.Value < 0) ||
                startsAtGameTime.HasValue && endsAtGameTime.HasValue && endsAtGameTime.Value < startsAtGameTime.Value)
                throw new ArgumentException("Invalid native conversation commitment timing.");
            Id = id; Kind = kind; Status = status; Revision = revision; ActionKind = actionKind;
            StartsAtGameTime = startsAtGameTime; EndsAtGameTime = endsAtGameTime;
        }
        internal static bool ValidText(string value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class ConversationContextSourceHandle
    {
        public string Owner { get; }
        public string Kind { get; }
        public long Revision { get; }
        public ConversationContextSourceHandle(string owner, string kind, long revision)
        {
            if (!ConversationContextCommitment.ValidText(owner, 128) || !ConversationContextCommitment.ValidText(kind, 64) || revision < 0)
                throw new ArgumentException("Invalid conversation read-source handle.");
            Owner = owner; Kind = kind; Revision = revision;
        }
    }

    public sealed class ConversationContextPersonalState
    {
        public ConversationContextFact<string> Race { get; }
        public ConversationContextFact<string> Gender { get; }
        public ConversationContextFact<bool> FamiliarWithPlayer { get; }
        public ConversationContextFact<string> KnownPlayerName { get; }
        public ConversationContextFact<bool> Grieving { get; }
        public ConversationContextFact<bool> Alarmed { get; }
        public NpcHouseholdKnowledge HouseholdKnowledge { get; }
        public ConversationContextFact<string> BodyContext { get; }
        public ConversationContextFact<string> TravelerContext { get; }

        public ConversationContextPersonalState(ConversationContextFact<string> race,
            ConversationContextFact<string> gender, ConversationContextFact<bool> familiarWithPlayer,
            ConversationContextFact<string> knownPlayerName, ConversationContextFact<bool> grieving,
            ConversationContextFact<bool> alarmed)
            : this(race, gender, familiarWithPlayer, knownPlayerName, grieving, alarmed, null)
        { }

        public ConversationContextPersonalState(ConversationContextFact<string> race,
            ConversationContextFact<string> gender, ConversationContextFact<bool> familiarWithPlayer,
            ConversationContextFact<string> knownPlayerName, ConversationContextFact<bool> grieving,
            ConversationContextFact<bool> alarmed, NpcHouseholdKnowledge householdKnowledge)
            : this(race, gender, familiarWithPlayer, knownPlayerName, grieving, alarmed, householdKnowledge, null)
        { }

        public ConversationContextPersonalState(ConversationContextFact<string> race,
            ConversationContextFact<string> gender, ConversationContextFact<bool> familiarWithPlayer,
            ConversationContextFact<string> knownPlayerName, ConversationContextFact<bool> grieving,
            ConversationContextFact<bool> alarmed, NpcHouseholdKnowledge householdKnowledge,
            ConversationContextFact<string> bodyContext)
            : this(race,gender,familiarWithPlayer,knownPlayerName,grieving,alarmed,householdKnowledge,bodyContext,null)
        { }

        public ConversationContextPersonalState(ConversationContextFact<string> race,
            ConversationContextFact<string> gender, ConversationContextFact<bool> familiarWithPlayer,
            ConversationContextFact<string> knownPlayerName, ConversationContextFact<bool> grieving,
            ConversationContextFact<bool> alarmed, NpcHouseholdKnowledge householdKnowledge,
            ConversationContextFact<string> bodyContext, ConversationContextFact<string> travelerContext)
        {
            if (race == null || gender == null || familiarWithPlayer == null || knownPlayerName == null || grieving == null || alarmed == null)
                throw new ArgumentException("Every personal-state value must be an explicit verified or unknown fact.");
            Race = race; Gender = gender; FamiliarWithPlayer = familiarWithPlayer; KnownPlayerName = knownPlayerName;
            Grieving = grieving; Alarmed = alarmed;
            HouseholdKnowledge = householdKnowledge;
            BodyContext = bodyContext ?? ConversationContextFact<string>.Unknown();
            TravelerContext = travelerContext ?? ConversationContextFact<string>.Unknown();
        }

        public static ConversationContextPersonalState Unknown() => new ConversationContextPersonalState(
            ConversationContextFact<string>.Unknown(), ConversationContextFact<string>.Unknown(),
            ConversationContextFact<bool>.Unknown(), ConversationContextFact<string>.Unknown(),
            ConversationContextFact<bool>.Unknown(), ConversationContextFact<bool>.Unknown());
    }

    public sealed class ConversationContextMood
    {
        public int Value { get; }
        public long AcceptedTurnSequence { get; }
        public long StateRevision { get; }

        public ConversationContextMood(int value, long acceptedTurnSequence)
            : this(value, acceptedTurnSequence, 0) { }

        public ConversationContextMood(int value, long acceptedTurnSequence, long stateRevision)
        {
            if (value < -100 || value > 100 || acceptedTurnSequence < 0 || stateRevision < 0)
                throw new ArgumentException("Invalid persistent conversation mood snapshot.");
            Value = value;
            AcceptedTurnSequence = acceptedTurnSequence;
            StateRevision = stateRevision;
        }
    }

    public sealed class ConversationContextSnapshot
    {
        private readonly ConversationContextSnapshot parentSnapshot;
        public ConversationContextBinding Binding { get; }
        public double CapturedGameTime { get; }
        public ConversationContextFact<string> ClockContext { get; }
        public ConversationContextFact<string> Name { get; }
        public ConversationContextFact<ConversationDialogueRole> Role { get; }
        public ConversationContextFact<string> PlayerScene { get; }
        public ConversationContextFact<string> NpcScene { get; }
        public ConversationContextFact<ConversationNpcPosition> NpcPosition { get; }
        public ConversationContextFact<ConversationNpcActivity> NpcActivity { get; }
        public ConversationContextFact<string> CurrentJourney { get; }
        public ConversationContextFact<bool> NpcOutdoors { get; }
        public ConversationContextFact<ConversationNpcWeatherObservation> Weather { get; }
        public ConversationContextFact<string> PersonalityTraits { get; }
        public int EffectiveDisposition { get; }
        public uint PersonalitySeed { get; }
        public ConversationContextPersonalState PersonalState { get; }
        public ConversationContextMood PersistentMood { get; }
        public NpcDirectedRelationshipView DirectedRelationship { get; }
        public NpcMotivationContextSnapshot MotivationContext { get; }
        public HouseholdOutingConversation HouseholdOuting { get; }
        public NpcAcuteFearView AcuteFear { get; }
        public bool VerifiedPlayerContact { get; }
        private readonly NpcRoleState roleEvidence;
        public NpcRoleState RoleEvidence { get { return roleEvidence == null ? null : NpcRoleState.CopyValidated(roleEvidence); } }
        public string MotivationBranchId => MotivationBranchIdFor(Binding);
        public ConversationLocationSnapshot PlayerLocationAtSubmission { get; }
        public IReadOnlyList<ConversationContextCommitment> Commitments { get; }
        public IReadOnlyList<ConversationContextSourceHandle> ReadSources { get; }
        public long Revision { get; }

        public ConversationContextSnapshot(ConversationContextBinding binding, double capturedGameTime,
            ConversationContextFact<string> clockContext, ConversationContextFact<string> name, ConversationContextFact<ConversationDialogueRole> role,
            ConversationContextFact<string> playerScene, ConversationContextFact<string> npcScene,
            ConversationContextFact<ConversationNpcPosition> npcPosition, ConversationContextFact<ConversationNpcActivity> npcActivity,
            ConversationContextFact<bool> npcOutdoors, ConversationContextFact<ConversationNpcWeatherObservation> weather,
            ConversationContextFact<string> personalityTraits, int disposition, uint personalitySeed,
            ConversationLocationSnapshot playerLocationAtSubmission,
            IEnumerable<ConversationContextCommitment> commitments, IEnumerable<ConversationContextSourceHandle> readSources)
            : this(binding, capturedGameTime, clockContext, name, role, playerScene, npcScene, npcPosition, npcActivity, npcOutdoors, weather,
                personalityTraits, disposition, personalitySeed, playerLocationAtSubmission, commitments, readSources,
                ConversationContextPersonalState.Unknown(), null, null, 0, null, null, null, false)
        { }

        public ConversationContextSnapshot(ConversationContextBinding binding, double capturedGameTime,
            ConversationContextFact<string> clockContext, ConversationContextFact<string> name, ConversationContextFact<ConversationDialogueRole> role,
            ConversationContextFact<string> playerScene, ConversationContextFact<string> npcScene,
            ConversationContextFact<ConversationNpcPosition> npcPosition, ConversationContextFact<ConversationNpcActivity> npcActivity,
            ConversationContextFact<bool> npcOutdoors, ConversationContextFact<ConversationNpcWeatherObservation> weather,
            ConversationContextFact<string> personalityTraits, int disposition, uint personalitySeed,
            ConversationLocationSnapshot playerLocationAtSubmission,
            IEnumerable<ConversationContextCommitment> commitments, IEnumerable<ConversationContextSourceHandle> readSources,
            ConversationContextPersonalState personalState)
            : this(binding, capturedGameTime, clockContext, name, role, playerScene, npcScene, npcPosition, npcActivity, npcOutdoors, weather,
                personalityTraits, disposition, personalitySeed, playerLocationAtSubmission, commitments, readSources,
                personalState, null, null, 0, null, null, null, false)
        { }

        public ConversationContextSnapshot(ConversationContextBinding binding, double capturedGameTime,
            ConversationContextFact<string> clockContext, ConversationContextFact<string> name, ConversationContextFact<ConversationDialogueRole> role,
            ConversationContextFact<string> playerScene, ConversationContextFact<string> npcScene,
            ConversationContextFact<ConversationNpcPosition> npcPosition, ConversationContextFact<ConversationNpcActivity> npcActivity,
            ConversationContextFact<bool> npcOutdoors, ConversationContextFact<ConversationNpcWeatherObservation> weather,
            ConversationContextFact<string> personalityTraits, int disposition, uint personalitySeed,
            ConversationLocationSnapshot playerLocationAtSubmission,
            IEnumerable<ConversationContextCommitment> commitments, IEnumerable<ConversationContextSourceHandle> readSources,
            ConversationContextPersonalState personalState, ConversationContextMood persistentMood)
            : this(binding, capturedGameTime, clockContext, name, role, playerScene, npcScene, npcPosition, npcActivity, npcOutdoors, weather,
                personalityTraits, disposition, personalitySeed, playerLocationAtSubmission, commitments, readSources,
                personalState, persistentMood, null, 0, null, null, null, false)
        { }

        private ConversationContextSnapshot(ConversationContextBinding binding, double capturedGameTime,
            ConversationContextFact<string> clockContext, ConversationContextFact<string> name, ConversationContextFact<ConversationDialogueRole> role,
            ConversationContextFact<string> playerScene, ConversationContextFact<string> npcScene,
            ConversationContextFact<ConversationNpcPosition> npcPosition, ConversationContextFact<ConversationNpcActivity> npcActivity,
            ConversationContextFact<bool> npcOutdoors, ConversationContextFact<ConversationNpcWeatherObservation> weather,
            ConversationContextFact<string> personalityTraits, int disposition, uint personalitySeed,
            ConversationLocationSnapshot playerLocationAtSubmission,
            IEnumerable<ConversationContextCommitment> commitments, IEnumerable<ConversationContextSourceHandle> readSources,
            ConversationContextPersonalState personalState, ConversationContextMood persistentMood,
            NpcDirectedRelationshipView directedRelationship, long revision, ConversationContextSnapshot parent,
            NpcMotivationContextSnapshot motivationContext, NpcAcuteFearView acuteFear, bool verifiedPlayerContact,
            NpcRoleState roleEvidence = null, HouseholdOutingConversation householdOuting = null,
            ConversationContextFact<string> currentJourney = null)
        {
            if (binding == null || !Finite(capturedGameTime) || capturedGameTime < 0 || clockContext == null || name == null || role == null || playerScene == null ||
                npcScene == null || npcPosition == null || npcActivity == null || npcOutdoors == null || weather == null || personalityTraits == null || personalState == null ||
                (persistentMood != null && binding.IdentityKind != ConversationIdentityKind.StablePerson) ||
                (directedRelationship != null && (binding.IdentityKind != ConversationIdentityKind.StablePerson ||
                    !string.Equals(directedRelationship.ObserverId, binding.PersonId, StringComparison.Ordinal) ||
                    !string.Equals(directedRelationship.TargetId, NpcDirectedRelationship.PlayerTargetId, StringComparison.Ordinal))) ||
                (motivationContext != null && (binding.IdentityKind != ConversationIdentityKind.StablePerson ||
                    !string.Equals(motivationContext.SaveBranchId, MotivationBranchIdFor(binding), StringComparison.Ordinal) ||
                    !string.Equals(motivationContext.ObserverId, binding.PersonId, StringComparison.Ordinal) ||
                    !string.Equals(motivationContext.Profile.PersonId, binding.PersonId, StringComparison.Ordinal) ||
                    motivationContext.CapturedGameTime != capturedGameTime)) ||
                disposition < -100 || disposition > 100 || revision < 0)
                throw new ArgumentException("Invalid authoritative conversation context snapshot or motivation binding.");
            if (!CapturedBy(clockContext, capturedGameTime) || !CapturedBy(name, capturedGameTime) || !CapturedBy(role, capturedGameTime) ||
                !CapturedBy(playerScene, capturedGameTime) || !CapturedBy(npcScene, capturedGameTime) || !CapturedBy(npcPosition, capturedGameTime) ||
                !CapturedBy(npcActivity, capturedGameTime) || !CapturedBy(npcOutdoors, capturedGameTime) || !CapturedBy(weather, capturedGameTime) ||
                !CapturedBy(personalityTraits, capturedGameTime) || !CapturedBy(personalState.Race, capturedGameTime) ||
                !CapturedBy(personalState.Gender, capturedGameTime) || !CapturedBy(personalState.FamiliarWithPlayer, capturedGameTime) ||
                !CapturedBy(personalState.KnownPlayerName, capturedGameTime) || !CapturedBy(personalState.Grieving, capturedGameTime) ||
                !CapturedBy(personalState.Alarmed, capturedGameTime) || !CapturedBy(personalState.BodyContext, capturedGameTime) ||
                !CapturedBy(personalState.TravelerContext, capturedGameTime))
                throw new ArgumentException("A conversation fact cannot be observed after its owning snapshot time.");
            Binding = binding; CapturedGameTime = capturedGameTime; ClockContext = clockContext; Name = name; Role = role; PlayerScene = playerScene;
            NpcScene = npcScene; NpcPosition = npcPosition; NpcActivity = npcActivity; NpcOutdoors = npcOutdoors; Weather = weather;
            PersonalityTraits = personalityTraits; EffectiveDisposition = disposition; PersonalitySeed = personalitySeed;
            if (personalState.HouseholdKnowledge != null &&
                !string.Equals(personalState.HouseholdKnowledge.PersonId, binding.PersonId, StringComparison.Ordinal))
                throw new ArgumentException("Household self-knowledge belongs to another speaker.");
            PersonalState = personalState;
            PersistentMood = persistentMood;
            DirectedRelationship = directedRelationship;
            MotivationContext = motivationContext;
            HouseholdOuting = householdOuting ?? (parent == null ? null : parent.HouseholdOuting);
            CurrentJourney = currentJourney ?? (parent == null ? ConversationContextFact<string>.Unknown() : parent.CurrentJourney);
            string journey;
            if (CurrentJourney.TryGetVerified(out journey) &&
                (!ConversationContextCommitment.ValidText(journey,384) || !CapturedBy(CurrentJourney,capturedGameTime)))
                throw new ArgumentException("Invalid captured current journey.");
            if (HouseholdOuting != null && (binding.IdentityKind != ConversationIdentityKind.StablePerson ||
                HouseholdOuting.SpeakerId != binding.PersonId || HouseholdOuting.CapturedAt != capturedGameTime))
                throw new ArgumentException("Shared outing facts belong to another speaker or request time.");
            AcuteFear = acuteFear;
            VerifiedPlayerContact = verifiedPlayerContact;
            var capturedRole = roleEvidence ?? (parent == null ? null : parent.roleEvidence);
            if (capturedRole != null && (binding.IdentityKind != ConversationIdentityKind.StablePerson ||
                capturedRole.PersonId != binding.PersonId))
                throw new ArgumentException("Role evidence belongs to another speaker.");
            this.roleEvidence = capturedRole == null ? null : NpcRoleState.CopyValidated(capturedRole);
            PlayerLocationAtSubmission = playerLocationAtSubmission == null ? null :
                new ConversationLocationSnapshot(playerLocationAtSubmission.CapturedAt, playerLocationAtSubmission.PlaceCopy());
            Commitments = CopyBounded(commitments, 64, "commitments");
            ReadSources = CopyBounded(readSources, 32, "read sources");
            Revision = revision; parentSnapshot = parent;
        }

        public ConversationContextSnapshot WithCommittedDisposition(int disposition)
        {
            if (disposition < -100 || disposition > 100) throw new ArgumentOutOfRangeException(nameof(disposition));
            if (disposition == EffectiveDisposition) throw new InvalidOperationException("A no-op disposition does not create a successor snapshot.");
            if (Revision == long.MaxValue) throw new InvalidOperationException("Conversation snapshot revision exhausted.");
            return new ConversationContextSnapshot(Binding, CapturedGameTime, ClockContext, Name, Role, PlayerScene, NpcScene, NpcPosition, NpcActivity,
                NpcOutdoors, Weather, PersonalityTraits, disposition, PersonalitySeed, PlayerLocationAtSubmission, Commitments, ReadSources,
                PersonalState, PersistentMood, DirectedRelationship, Revision + 1, this, MotivationContext, AcuteFear, VerifiedPlayerContact);
        }

        public ConversationContextSnapshot WithCurrentJourney(ConversationContextFact<string> journey)
        {
            if(journey==null)throw new ArgumentNullException(nameof(journey));
            if(Revision==long.MaxValue)throw new InvalidOperationException("Conversation snapshot revision exhausted.");
            return new ConversationContextSnapshot(Binding,CapturedGameTime,ClockContext,Name,Role,PlayerScene,NpcScene,
                NpcPosition,NpcActivity,NpcOutdoors,Weather,PersonalityTraits,EffectiveDisposition,PersonalitySeed,
                PlayerLocationAtSubmission,Commitments,ReadSources,PersonalState,PersistentMood,DirectedRelationship,
                Revision+1,this,MotivationContext,AcuteFear,VerifiedPlayerContact,currentJourney:journey);
        }

        public ConversationContextSnapshot WithHouseholdOuting(HouseholdOutingConversation outing)
        {
            if (outing == null) throw new ArgumentNullException(nameof(outing));
            if (ReferenceEquals(outing, HouseholdOuting)) throw new InvalidOperationException("No new shared outing facts.");
            if (Revision == long.MaxValue) throw new InvalidOperationException("Conversation snapshot revision exhausted.");
            return new ConversationContextSnapshot(Binding, CapturedGameTime, ClockContext, Name, Role, PlayerScene, NpcScene,
                NpcPosition, NpcActivity, NpcOutdoors, Weather, PersonalityTraits, EffectiveDisposition, PersonalitySeed,
                PlayerLocationAtSubmission, Commitments, ReadSources, PersonalState, PersistentMood, DirectedRelationship,
                Revision + 1, this, MotivationContext, AcuteFear, VerifiedPlayerContact, null, outing);
        }

        public ConversationContextSnapshot WithDirectedRelationship(NpcDirectedRelationshipView relationship)
        {
            if (relationship == null) throw new ArgumentNullException(nameof(relationship));
            if (ReferenceEquals(relationship, DirectedRelationship))
                throw new InvalidOperationException("A no-op relationship attachment does not create a successor snapshot.");
            if (Revision == long.MaxValue) throw new InvalidOperationException("Conversation snapshot revision exhausted.");
            return new ConversationContextSnapshot(Binding, CapturedGameTime, ClockContext, Name, Role, PlayerScene, NpcScene,
                NpcPosition, NpcActivity, NpcOutdoors, Weather, PersonalityTraits, EffectiveDisposition, PersonalitySeed,
                PlayerLocationAtSubmission, Commitments, ReadSources, PersonalState, PersistentMood, relationship, Revision + 1, this,
                MotivationContext, AcuteFear, VerifiedPlayerContact);
        }

        public ConversationContextSnapshot WithMotivationContext(NpcMotivationContextSnapshot motivationContext)
        {
            if (motivationContext == null) throw new ArgumentNullException(nameof(motivationContext));
            if (ReferenceEquals(motivationContext, MotivationContext))
                throw new InvalidOperationException("A no-op motivation attachment does not create a successor snapshot.");
            if (Revision == long.MaxValue) throw new InvalidOperationException("Conversation snapshot revision exhausted.");
            return new ConversationContextSnapshot(Binding, CapturedGameTime, ClockContext, Name, Role, PlayerScene, NpcScene,
                NpcPosition, NpcActivity, NpcOutdoors, Weather, PersonalityTraits, EffectiveDisposition, PersonalitySeed,
                PlayerLocationAtSubmission, Commitments, ReadSources, PersonalState, PersistentMood, DirectedRelationship,
                Revision + 1, this, motivationContext, AcuteFear, VerifiedPlayerContact);
        }

        public ConversationContextSnapshot WithFearContext(NpcAcuteFearView acuteFear, bool verifiedPlayerContact)
        {
            if (acuteFear != null && (Binding.IdentityKind != ConversationIdentityKind.StablePerson ||
                !string.Equals(acuteFear.ObserverId, Binding.PersonId, StringComparison.Ordinal) ||
                acuteFear.ProjectedGameTime != CapturedGameTime))
                throw new ArgumentException("Acute fear must match this stable observer and exact snapshot time.", nameof(acuteFear));
            if (ReferenceEquals(acuteFear, AcuteFear) && verifiedPlayerContact == VerifiedPlayerContact)
                throw new InvalidOperationException("A no-op fear context attachment does not create a successor snapshot.");
            if (Revision == long.MaxValue) throw new InvalidOperationException("Conversation snapshot revision exhausted.");
            return new ConversationContextSnapshot(Binding, CapturedGameTime, ClockContext, Name, Role, PlayerScene, NpcScene,
                NpcPosition, NpcActivity, NpcOutdoors, Weather, PersonalityTraits, EffectiveDisposition, PersonalitySeed,
                PlayerLocationAtSubmission, Commitments, ReadSources, PersonalState, PersistentMood, DirectedRelationship,
                Revision + 1, this, MotivationContext, acuteFear, verifiedPlayerContact);
        }

        public bool IsSuccessorOf(ConversationContextSnapshot parent) => ReferenceEquals(parentSnapshot, parent) && parent != null &&
            Revision == parent.Revision + 1 && Binding.HasSameRequestBinding(parent.Binding);

        public ConversationContextSnapshot WithRoleEvidence(NpcRoleState evidence)
        {
            if (evidence == null) throw new ArgumentNullException(nameof(evidence));
            if (Revision == long.MaxValue) throw new InvalidOperationException("Conversation snapshot revision exhausted.");
            return new ConversationContextSnapshot(Binding, CapturedGameTime, ClockContext, Name, Role, PlayerScene, NpcScene,
                NpcPosition, NpcActivity, NpcOutdoors, Weather, PersonalityTraits, EffectiveDisposition, PersonalitySeed,
                PlayerLocationAtSubmission, Commitments, ReadSources, PersonalState, PersistentMood, DirectedRelationship,
                Revision + 1, this, MotivationContext, AcuteFear, VerifiedPlayerContact, evidence);
        }

        private static ReadOnlyCollection<T> CopyBounded<T>(IEnumerable<T> values, int max, string label) where T : class
        {
            if (values == null) throw new ArgumentException("Conversation " + label + " are required.");
            var copy = values.ToList();
            if (copy.Count > max || copy.Any(value => value == null)) throw new ArgumentException("Invalid conversation " + label + ".");
            return new ReadOnlyCollection<T>(copy);
        }
        private static bool CapturedBy<T>(ConversationContextFact<T> fact, double capturedGameTime)
        { return fact.Status != ConversationContextFactStatus.Verified || fact.ObservedAt <= capturedGameTime; }
        private static string MotivationBranchIdFor(ConversationContextBinding binding)
        { return binding.SaveEpoch.ToString(CultureInfo.InvariantCulture) + ":" + binding.ArchiveId; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class ConversationContextTurn
    {
        public ConversationTextSpeaker Speaker { get; }
        public string Text { get; }
        public ConversationContextTurn(ConversationTextSpeaker speaker, string text)
        {
            if (!Enum.IsDefined(typeof(ConversationTextSpeaker), speaker) || text == null || text.Length > 8192)
                throw new ArgumentException("Invalid attributed conversation turn.");
            Speaker = speaker; Text = text;
        }
    }
}
