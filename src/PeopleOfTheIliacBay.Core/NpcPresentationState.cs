using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Immutable read-only NPC state captured for one face and voice presentation.</summary>
    public sealed class NpcPresentationState
    {
        private const int MaximumPersonIdLength = 240;

        public string PersonId { get; private set; }
        public double GameTime { get; private set; }
        public int Mood { get; private set; }
        public bool DispositionKnown { get; private set; }
        public int Disposition { get; private set; }
        public bool RelationshipKnown { get; private set; }
        public bool TrustKnown { get; private set; }
        public int Trust { get; private set; }
        public bool PlayerFearKnown { get; private set; }
        public int PlayerFear { get; private set; }
        public int Resentment { get; private set; }
        public bool AcuteFearKnown { get; private set; }
        public double AcuteFearIntensity { get; private set; }
        public int DeliveryFear { get; private set; }
        public int Encounters { get; private set; }
        public IReadOnlyDictionary<string, string> SavedTraits { get; private set; }

        private NpcPresentationState(string personId, double gameTime, int mood,
            bool dispositionKnown, int disposition, bool relationshipKnown, bool trustKnown,
            int trust, bool playerFearKnown, int playerFear, int resentment,
            bool acuteFearKnown, double acuteFearIntensity, int encounters,
            IReadOnlyDictionary<string, string> savedTraits)
        {
            PersonId = personId;
            GameTime = gameTime;
            Mood = mood;
            DispositionKnown = dispositionKnown;
            Disposition = disposition;
            RelationshipKnown = relationshipKnown;
            TrustKnown = trustKnown;
            Trust = trust;
            PlayerFearKnown = playerFearKnown;
            PlayerFear = playerFear;
            Resentment = resentment;
            AcuteFearKnown = acuteFearKnown;
            AcuteFearIntensity = acuteFearIntensity;
            DeliveryFear = Math.Max(playerFear,
                (int)Math.Round(acuteFearIntensity, MidpointRounding.AwayFromZero));
            Encounters = encounters;
            SavedTraits = savedTraits;
        }

        /// <summary>Captures native-backed presentation values without creating profiles or state rows.</summary>
        public static NpcPresentationState Capture(WorldStateStore store, string personId,
            double gameTime, bool verifiedPlayerContact)
        {
            if (store == null || !ValidIdentity(personId) || !Finite(gameTime) || gameTime < 0)
                return null;

            var mood = store.CaptureConversationMood(personId, gameTime);
            bool dispositionKnown = store.TryGetActualConversationDisposition(personId, out int actualDisposition);
            int disposition = dispositionKnown ? (actualDisposition + 100) / 2 : 0;

            var relationship = store.ReadNpcRelationship(personId, NpcDirectedRelationship.PlayerTargetId, gameTime);
            bool relationshipKnown = relationship != null;
            bool trustKnown = relationshipKnown && relationship.TrustKnown;
            int trust = trustKnown ? relationship.ReliabilityTrust : 0;
            bool playerFearKnown = relationshipKnown;
            int playerFear = playerFearKnown
                ? NpcPlayerContactFearPolicy.Project(relationship.ProjectedInterpersonalFear, verifiedPlayerContact)
                : 0;
            int resentment = relationshipKnown ? relationship.ProjectedResentment : 0;

            var acuteFear = store.ReadNpcAcuteFear(personId, gameTime);
            bool acuteFearKnown = acuteFear != null;
            double acuteFearIntensity = acuteFearKnown ? acuteFear.CurrentFear : 0;
            var profile = store.ReadNpcPersonality(personId);
            IReadOnlyDictionary<string, string> traits = profile == null
                ? EmptyTraits()
                : CopyTraits(profile.SelectedTraits);

            return new NpcPresentationState(personId, gameTime, mood.Value,
                dispositionKnown, disposition, relationshipKnown, trustKnown, trust,
                playerFearKnown, playerFear, resentment, acuteFearKnown, acuteFearIntensity,
                store.ConversationEncounters(personId), traits);
        }

        /// <summary>Creates voice delivery context from this capture and its already-saved personality traits.</summary>
        public BreezeTtsTrialDeliveryContext CreateDeliveryContext()
        {
            if (SavedTraits.Count == 0) return null;
            return new BreezeTtsTrialDeliveryContext(PersonId, Mood, DispositionKnown, Disposition,
                TrustKnown, Trust, DeliveryFear, Resentment, Encounters, SavedTraits);
        }

        private static IReadOnlyDictionary<string, string> EmptyTraits()
        {
            return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));
        }

        private static IReadOnlyDictionary<string, string> CopyTraits(
            IReadOnlyDictionary<string, string> source)
        {
            var copy = new Dictionary<string, string>(source.Count, StringComparer.Ordinal);
            foreach (var trait in source) copy.Add(trait.Key, trait.Value);
            return new ReadOnlyDictionary<string, string>(copy);
        }

        private static bool ValidIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumPersonIdLength) return false;
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i])) return false;
            return true;
        }

        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
