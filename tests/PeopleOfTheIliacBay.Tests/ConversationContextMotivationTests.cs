using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ConversationContextMotivationTests
    {
        private const string PersonId = "npc:motivation-observer";
        private const string ArchiveId = "0123456789abcdef0123456789abcdef";
        private const double CapturedTime = 100;

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("motivation context attachment binds branch person and exact capture time", AttachmentBinding));
            cases.Add(("motivation context survives immutable conversation successors", SuccessorRetention));
            cases.Add(("all model projections share the bounded motivation block without duplicating traits", SharedProjection));
            cases.Add(("small action voice budget omits whole motivation block instead of failing or cutting a fact", SmallActionVoiceBudget));
        }

        private static void AttachmentBinding()
        {
            var snapshot = Snapshot();
            Equal("7:" + ArchiveId, ReadMotivationBranchId(snapshot));
            var context = MotivationContext("7:" + ArchiveId, PersonId, CapturedTime);
            var attached = Attach(snapshot, context);
            Same(context, Property(attached, "MotivationContext"));

            Throws<ArgumentException>(() => Attach(snapshot, MotivationContext("8:" + ArchiveId, PersonId, CapturedTime)));
            Throws<ArgumentException>(() => Attach(snapshot, MotivationContext("7:" + ArchiveId, "npc:other", CapturedTime)));
            Throws<ArgumentException>(() => Attach(snapshot, MotivationContext("7:" + ArchiveId, PersonId, CapturedTime + 1)));
            Throws<ArgumentException>(() => Attach(Snapshot(ephemeral: true), context));
        }

        private static void SuccessorRetention()
        {
            var snapshot = Attach(Snapshot(), MotivationContext("7:" + ArchiveId, PersonId, CapturedTime));
            var disposition = snapshot.WithCommittedDisposition(snapshot.EffectiveDisposition + 1);
            Same(Property(snapshot, "MotivationContext"), Property(disposition, "MotivationContext"));
            True(disposition.IsSuccessorOf(snapshot), "disposition successor lineage lost");

            var relationship = NpcDirectedRelationship.Create(PersonId, NpcDirectedRelationship.PlayerTargetId).ToView();
            var withRelationship = snapshot.WithDirectedRelationship(relationship);
            Same(Property(snapshot, "MotivationContext"), Property(withRelationship, "MotivationContext"));
            True(withRelationship.IsSuccessorOf(snapshot), "relationship successor lineage lost");
        }

        private static void SharedProjection()
        {
            var snapshot = Attach(Snapshot(), MotivationContext("7:" + ArchiveId, PersonId, CapturedTime));
            const int budget = 12000;
            string motivation = NpcMotivationContextFormatter.ForModels((NpcMotivationContextSnapshot)Property(snapshot, "MotivationContext"));
            string social = ConversationContextFormatter.ForSocialReply(snapshot, Array.Empty<ConversationContextTurn>(), "hello", budget);
            string action = ConversationContextFormatter.ForActionVoice(snapshot, budget);
            string semantic = ConversationContextFormatter.ForSemanticClassifier(snapshot, Array.Empty<ConversationContextTurn>(), "hello", budget);
            Contains(social, motivation);
            Contains(action, motivation);
            Contains(semantic, motivation);
            Equal(1, Count(semantic, "Known personality: reserved, plainspoken."));
            True(social.Length <= budget && action.Length <= budget && semantic.Length <= budget, "projection exceeded requested budget");

            string compactSocial = ConversationContextFormatter.ForSocialReply(snapshot, 3072);
            string compactAction = ConversationContextFormatter.ForActionVoice(snapshot, 2400);
            string compactSemantic = ConversationContextFormatter.ForSemanticClassifier(snapshot, 2048);
            foreach (string compact in new[] { compactSocial, compactAction, compactSemantic })
            {
                Contains(compact, "Captured game clock:");
                Contains(compact, "Known personality:");
                Contains(compact, "Relationship toward player:");
                Contains(compact, "Shared actor-known motivation context:");
                True(compact.Length <= (compact == compactSocial ? 3072 : compact == compactAction ? 2400 : 2048),
                    "production projection exceeded its requested character budget");
            }
            string unavailable = ConversationContextFormatter.ForSemanticClassifier(Snapshot(), 2048);
            Contains(unavailable, "Known personality: reserved, plainspoken.");
            Contains(unavailable, "Derived mood from tracked inputs:");
            Contains(unavailable, "Relationship toward player: unknown");
            Contains(unavailable, "Agenda and paid duties: unknown");
        }

        private static void SmallActionVoiceBudget()
        {
            var snapshot = Attach(Snapshot(), MotivationContext("7:" + ArchiveId, PersonId, CapturedTime));
            const int budget = 1200;
            string result = ConversationContextFormatter.ForActionVoice(snapshot, budget);
            True(result.Length <= budget, "action voice projection exceeded its budget");
            Contains(result, "Speaker: Michell Farr");
            Contains(result, "Captured game clock: clock");

            string wholeMotivation = "Shared actor-known motivation context:\n" +
                NpcMotivationContextFormatter.ForModels((NpcMotivationContextSnapshot)Property(snapshot, "MotivationContext"), 1024);
            if (result.Contains("Shared actor-known motivation context:", StringComparison.Ordinal))
                Contains(result, wholeMotivation);
            else
                Contains(result, "context omitted");
        }

        private static ConversationContextSnapshot Snapshot(bool ephemeral = false)
        {
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile,
                ephemeral ? null : PersonId, ephemeral ? "speaker:ephemeral" : null, "session:motivation", 3, 7,
                ArchiveId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "scene:one", 4);
            return new ConversationContextSnapshot(binding, CapturedTime,
                StringFact("clock"), StringFact("Michell Farr"), EnumFact<ConversationDialogueRole>(ConversationDialogueRole.ManagedResident),
                StringFact("Exterior"), StringFact(null),
                ConversationContextFact<ConversationNpcPosition>.Unknown(), ConversationContextFact<ConversationNpcActivity>.Unknown(),
                ConversationContextFact<bool>.Unknown(), ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(),
                StringFact("reserved, plainspoken"), 10, 77u, null,
                Array.Empty<ConversationContextCommitment>(), Array.Empty<ConversationContextSourceHandle>());
        }

        private static ConversationContextFact<string> StringFact(string value)
        {
            return value == null ? ConversationContextFact<string>.Unknown() :
                ConversationContextFact<string>.Verified(value, ConversationContextFactSource.Save, CapturedTime);
        }

        private static ConversationContextFact<T> EnumFact<T>(T value) where T : struct
        {
            return ConversationContextFact<T>.Verified(value, ConversationContextFactSource.Save, CapturedTime);
        }

        private static NpcMotivationContextSnapshot MotivationContext(string branch, string person, double time)
        {
            var clockEvidence = Evidence(branch, person, "clock", NpcMotivationEvidenceKind.WorldClock, 2, time);
            var profileEvidence = Evidence(branch, person, "profile", NpcMotivationEvidenceKind.PersonalityProfile, 3, time);
            var clock = new NpcMotivationGameClock(time, 2, clockEvidence);
            var profile = new NpcMotivationProfileReference(person, 3, profileEvidence);
            return NpcMotivationContextSnapshot.Capture(branch, person, 4, clock, profile, null,
                null, null, null, null, null, null, null);
        }

        private static NpcMotivationEvidenceRef Evidence(string branch, string person, string id,
            NpcMotivationEvidenceKind kind, long revision, double time)
        { return new NpcMotivationEvidenceRef(branch, person, id, kind, revision, time); }

        private static ConversationContextSnapshot Attach(ConversationContextSnapshot snapshot, NpcMotivationContextSnapshot context)
        {
            var method = typeof(ConversationContextSnapshot).GetMethod("WithMotivationContext", BindingFlags.Public | BindingFlags.Instance);
            True(method != null, "Missing public WithMotivationContext successor API");
            try { return (ConversationContextSnapshot)method.Invoke(snapshot, new object[] { context }); }
            catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
        }

        private static string ReadMotivationBranchId(ConversationContextSnapshot snapshot)
        {
            var property = typeof(ConversationContextSnapshot).GetProperty("MotivationBranchId", BindingFlags.Public | BindingFlags.Instance);
            True(property != null, "Missing MotivationBranchId");
            return (string)property.GetValue(snapshot);
        }

        private static object Property(object target, string name)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            True(property != null, "Missing public property " + name);
            return property.GetValue(target);
        }

        private static int Count(string value, string fragment)
        {
            int count = 0, at = 0;
            while ((at = value.IndexOf(fragment, at, StringComparison.Ordinal)) >= 0) { count++; at += fragment.Length; }
            return count;
        }
        private static void Contains(string value, string fragment)
        { True(value.Contains(fragment, StringComparison.Ordinal), "Missing expected projection content: " + fragment); }
        private static void Same(object expected, object actual)
        { True(ReferenceEquals(expected, actual), "Expected immutable attachment identity to be preserved."); }
        private static void Equal<T>(T expected, T actual)
        { True(EqualityComparer<T>.Default.Equals(expected, actual), "Expected " + expected + ", got " + actual + "."); }
        private static void True(bool condition, string message = "Assertion failed.")
        { if (!condition) throw new InvalidOperationException(message); }
        private static void Throws<T>(Action body) where T : Exception
        {
            try { body(); }
            catch (TargetInvocationException error) when (error.InnerException is T) { return; }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }
    }
}
