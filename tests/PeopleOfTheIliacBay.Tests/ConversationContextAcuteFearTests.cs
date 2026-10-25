using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ConversationContextAcuteFearTests
    {
        private const double CapturedTime = 1000;
        private const string ArchiveId = "0123456789abcdef0123456789abcdef";

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Acute fear attachment binds stable observer and exact capture time", AttachmentBinding));
            cases.Add(("Fear context survives every immutable snapshot successor", SuccessorRetention));
            cases.Add(("Acute danger and player-directed fear keep separate provenance", FearProjectionKeepsProvenance));
            cases.Add(("Verified contact does not fabricate unseen acute danger", ContactWithoutAcuteFear));
            cases.Add(("Acute fear provenance remains atomic under tight context budgets", BoundedFearProjection));
        }

        private static void AttachmentBinding()
        {
            NpcAcuteFearView fear = ReadFear("hostile:session-77", CapturedTime, out string observer);
            ConversationContextSnapshot original = Snapshot(observer, CapturedTime);
            ConversationContextSnapshot attached = original.WithFearContext(fear, true);
            Same(fear, attached.AcuteFear);
            True(attached.VerifiedPlayerContact);
            True(attached.IsSuccessorOf(original));
            True(original.AcuteFear == null && !original.VerifiedPlayerContact,
                "Fear attachment must not mutate its parent snapshot.");

            Throws<ArgumentException>(() => Snapshot("npc:other", CapturedTime).WithFearContext(fear, true));
            NpcAcuteFearView later = ReadFear("hostile:session-77", CapturedTime + 1, out _);
            Throws<ArgumentException>(() => original.WithFearContext(later, true));
            Throws<ArgumentException>(() => Snapshot(null, CapturedTime).WithFearContext(fear, true));

            foreach (var property in typeof(ConversationContextSnapshot).GetProperties())
                if (property.Name == "AcuteFear" || property.Name == "VerifiedPlayerContact")
                    True(property.GetSetMethod(false) == null, "Fear context snapshot property must be read-only: " + property.Name);
        }

        private static void SuccessorRetention()
        {
            NpcAcuteFearView fear = ReadFear("hostile:successor", CapturedTime, out string observer);
            ConversationContextSnapshot attached = Snapshot(observer, CapturedTime).WithFearContext(fear, true);
            ConversationContextSnapshot disposition = attached.WithCommittedDisposition(attached.EffectiveDisposition + 1);
            Retains(attached, disposition, fear);

            var relationshipState = NpcDirectedRelationship.Create(observer, NpcDirectedRelationship.PlayerTargetId);
            relationshipState.InterpersonalFear = 14;
            relationshipState.LastGameTime = -1;
            ConversationContextSnapshot relationship = attached.WithDirectedRelationship(relationshipState.ToView());
            Retains(attached, relationship, fear);

            ConversationContextSnapshot motivation = attached.WithMotivationContext(Motivation(observer, CapturedTime));
            Retains(attached, motivation, fear);
        }

        private static void FearProjectionKeepsProvenance()
        {
            NpcAcuteFearView fear = ReadFear("hostile:unrelated", CapturedTime, out string observer);
            var relationshipState = NpcDirectedRelationship.Create(observer, NpcDirectedRelationship.PlayerTargetId);
            relationshipState.InterpersonalFear = 24;
            relationshipState.LastGameTime = -1;
            ConversationContextSnapshot snapshot = Snapshot(observer, CapturedTime)
                .WithDirectedRelationship(relationshipState.ToView()).WithFearContext(fear, true);

            string[] projections =
            {
                ConversationContextFormatter.ForSocialReply(snapshot, Array.Empty<ConversationContextTurn>(), "hello", 8192),
                ConversationContextFormatter.ForActionVoice(snapshot, 8192),
                ConversationContextFormatter.ForSemanticClassifier(snapshot, Array.Empty<ConversationContextTurn>(), "hello", 8192)
            };
            foreach (string projection in projections)
            {
                True(projection.Contains("interpersonal fear=24/100") &&
                    projection.Contains("Verified player-contact fear projection=30/100"),
                    "Contact projection must preserve stored directed-fear residual and show amplified contact fear separately.");
                True(projection.IndexOf("non-player danger fear", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Acute danger must remain a distinct fact from interpersonal fear.");
                True(projection.Contains(fear.SourceId) && projection.Contains("1000") &&
                    projection.IndexOf("Severe", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Acute fear must retain source, severity, and observed game-time provenance.");
                True(projection.IndexOf("you caused", StringComparison.OrdinalIgnoreCase) < 0 &&
                    projection.IndexOf("threat defeated", StringComparison.OrdinalIgnoreCase) < 0 &&
                    projection.IndexOf("threat remains active", StringComparison.OrdinalIgnoreCase) < 0,
                    "Acute observation must not blame the player or claim current threat status.");
            }
        }

        private static void ContactWithoutAcuteFear()
        {
            string observer;
            _ = ReadFear("hostile:discarded", CapturedTime, out observer);
            var relationshipState = NpcDirectedRelationship.Create(observer, NpcDirectedRelationship.PlayerTargetId);
            relationshipState.InterpersonalFear = 24;
            relationshipState.LastGameTime = -1;
            ConversationContextSnapshot snapshot = Snapshot(observer, CapturedTime)
                .WithDirectedRelationship(relationshipState.ToView()).WithFearContext(null, true);
            string text = ConversationContextFormatter.ForSocialReply(snapshot, Array.Empty<ConversationContextTurn>(), "hello", 8192);
            True(snapshot.AcuteFear == null && snapshot.VerifiedPlayerContact,
                "Contact remains valid when no unrelated danger observation exists.");
            True(text.Contains("interpersonal fear=24/100") &&
                text.Contains("Verified player-contact fear projection=30/100"),
                "Verified contact may amplify directed fear without changing stored residual.");
            True(text.IndexOf("non-player danger fear", StringComparison.OrdinalIgnoreCase) < 0,
                "Player contact must not create acute fear or a fictional hostile source.");
        }

        private static void BoundedFearProjection()
        {
            string source = "hostile:" + new string('x', 160);
            NpcAcuteFearView fear = ReadFear(source, CapturedTime, out string observer);
            ConversationContextSnapshot snapshot = Snapshot(observer, CapturedTime).WithFearContext(fear, false);
            const int budget = 256;
            string text = ConversationContextFormatter.ForActionVoice(snapshot, budget);
            True(text.Length <= budget, "Acute-fear context exceeded requested character budget.");
            if (text.Contains("Recent non-player danger fear:"))
                True(text.Contains(source) && text.Contains("last observed at game time 1000"),
                    "Formatter must retain complete acute-fear provenance as one row.");
            else
                True(text.Contains("context omitted to fit the character budget."),
                    "Omitted acute-fear provenance must be represented by the bounded omission marker.");
        }

        private static void Retains(ConversationContextSnapshot source, ConversationContextSnapshot successor,
            NpcAcuteFearView fear)
        {
            Same(fear, successor.AcuteFear);
            True(successor.VerifiedPlayerContact);
            True(successor.IsSuccessorOf(source), "Snapshot successor lineage changed while retaining fear context.");
        }

        private static ConversationContextSnapshot Snapshot(string personId, double time)
        {
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, personId,
                personId == null ? "speaker:ephemeral" : null, "session:acute-fear", 1, 7,
                ArchiveId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "scene:town", 3);
            var role = ConversationContextFact<ConversationDialogueRole>.Verified(ConversationDialogueRole.ManagedResident,
                ConversationContextFactSource.NativeActor, time);
            return new ConversationContextSnapshot(binding, time,
                Text("clock", time), Text("Mira", time), role, Text("Exterior", time),
                ConversationContextFact<string>.Unknown(), ConversationContextFact<ConversationNpcPosition>.Unknown(),
                ConversationContextFact<ConversationNpcActivity>.Unknown(), ConversationContextFact<bool>.Unknown(),
                ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(), Text("reserved", time),
                0, 7, null, Array.Empty<ConversationContextCommitment>(), Array.Empty<ConversationContextSourceHandle>());
        }

        private static ConversationContextFact<string> Text(string value, double time)
        { return ConversationContextFact<string>.Verified(value, ConversationContextFactSource.Save, time); }

        private static NpcAcuteFearView ReadFear(string sourceId, double time, out string observer)
        {
            var store = new WorldStateStore(42);
            observer = store.GetOrCreate(10, new[] { 20 }, 1).Households[0].Residents[0].Id;
            True(store.TryObserveNpcAcuteFear(observer, sourceId, NpcFearSeverity.Severe, time),
                "Test fixture must create one verified non-player danger observation.");
            NpcAcuteFearView fear = store.ReadNpcAcuteFear(observer, time);
            True(fear != null, "Test fixture must capture immutable acute fear at snapshot time.");
            return fear;
        }

        private static NpcMotivationContextSnapshot Motivation(string observer, double time)
        {
            string branch = "7:" + ArchiveId;
            var clockEvidence = new NpcMotivationEvidenceRef(branch, observer, "clock",
                NpcMotivationEvidenceKind.WorldClock, 2, time);
            var profileEvidence = new NpcMotivationEvidenceRef(branch, observer, "profile",
                NpcMotivationEvidenceKind.PersonalityProfile, 3, time);
            var clock = new NpcMotivationGameClock(time, 2, clockEvidence);
            var profile = new NpcMotivationProfileReference(observer, 3, profileEvidence);
            return NpcMotivationContextSnapshot.Capture(branch, observer, 4, clock, profile, null,
                null, null, null, null, null, null, null);
        }

        private static void Same(object expected, object actual)
        { True(ReferenceEquals(expected, actual), "Expected exact immutable acute-fear view identity to be retained."); }

        private static void True(bool condition, string message = "Condition was false.")
        { if (!condition) throw new InvalidOperationException(message); }

        private static void Throws<T>(Action body) where T : Exception
        {
            try { body(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }
    }
}
