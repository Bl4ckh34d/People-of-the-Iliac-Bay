using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ConversationPersistentMoodContextTests
    {
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Persistent conversation mood is immutable and bounded", MoodValueIsImmutable));
            cases.Add(("Persistent mood replaces daily and disposition outlook in context", PersistentMoodWins));
            cases.Add(("Persistent mood retains verified grief, alarm, and NPC-scene weather facts", PersistentMoodKeepsFacts));
        }

        private static Type MoodType()
        {
            var type = typeof(WorldStateStore).Assembly.GetType("PeopleOfTheIliacBay.Core.ConversationContextMood", false);
            Check.True(type != null, "Missing immutable ConversationContextMood projection");
            return type;
        }

        private static object Mood(int value, long sequence)
        {
            var type = MoodType();
            var constructor = type.GetConstructor(new[] { typeof(int), typeof(long) });
            Check.True(constructor != null, "ConversationContextMood must capture value and accepted sequence");
            try { return constructor.Invoke(new object[] { value, sequence }); }
            catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
        }

        private static void MoodValueIsImmutable()
        {
            var type = MoodType();
            object mood = Mood(-37, 12);
            Check.Equal(-37, (int)type.GetProperty("Value", PublicInstance).GetValue(mood));
            Check.Equal(12L, (long)type.GetProperty("AcceptedTurnSequence", PublicInstance).GetValue(mood));
            foreach (var property in type.GetProperties(PublicInstance))
                Check.True(!property.CanWrite, "Persistent mood projection must be immutable: " + property.Name);
            Check.Throws<ArgumentException>(() => Mood(-101, 0));
            Check.Throws<ArgumentException>(() => Mood(101, 0));
            Check.Throws<ArgumentException>(() => Mood(0, -1));
            Check.Throws<ArgumentException>(() => Snapshot(Mood(0, 0), 0, 100, false, false, false,
                "scene-a", 3, personId: null));
        }

        private static void PersistentMoodWins()
        {
            object firstMood = Mood(-37, 4);
            var first = Snapshot(firstMood, disposition: 85, gameTime: 100, grieving: false, alarmed: false,
                outdoors: false, weatherScene: "scene-a", weatherRevision: 3);
            var dispositionSuccessor = first.WithCommittedDisposition(16);
            var changedDayAndDisposition = Snapshot(Mood(-37, 4), disposition: -85, gameTime: 4 * 86400d + 36000,
                grieving: false, alarmed: false, outdoors: false, weatherScene: "scene-a", weatherRevision: 3);

            string social = Format(first, "ForSocialReply");
            string semantic = Format(first, "ForSemanticClassifier");
            string action = Format(first, "ForActionVoice");
            string successorSocial = Format(dispositionSuccessor, "ForSocialReply");
            string laterSocial = Format(changedDayAndDisposition, "ForSocialReply");
            string outlook = "Persistent conversation mood: -37/100 (low).";

            foreach (string text in new[] { social, semantic, action, successorSocial, laterSocial })
            {
                Check.True(text.Contains(outlook), "Persistent numeric mood must ground every dialogue projection");
                Check.True(text.Contains("separate from disposition and personality"),
                    "Persistent mood must be described independently from relationship disposition and traits");
                Check.True(!text.Contains("Derived mood from tracked inputs:") && !text.Contains("Current mood:"),
                    "Persistent mood must suppress daily and disposition-derived mood");
                Check.True(text.Contains("Known personality: reserved, plainspoken."),
                    "Persistent mood must remain separate from stable personality");
            }
            Check.True(social.Contains("Disposition: 85.") && laterSocial.Contains("Disposition: -85."),
                "Disposition remains a separate captured relationship fact");
            Check.True(ReferenceEquals(first.PersistentMood, dispositionSuccessor.PersistentMood) &&
                successorSocial.Contains("Disposition: 16."),
                "Disposition successor must preserve exact captured mood value and accepted sequence");
        }

        private static void PersistentMoodKeepsFacts()
        {
            var current = Snapshot(Mood(22, 5), disposition: 0, gameTime: 21 * 3600,
                grieving: true, alarmed: true, outdoors: true, weatherScene: "scene-a", weatherRevision: 3);
            string currentText = Format(current, "ForSocialReply");
            Check.True(currentText.Contains("Persistent conversation mood: 22/100 (positive)."));
            Check.True(currentText.Contains("grieving") && currentText.Contains("alarmed"),
                "Verified grief and alarm remain explicit facts beside saved mood");
            Check.True(currentText.Contains("NPC weather: fog."), "Verified current NPC weather remains in context");

            var staleWeather = Snapshot(Mood(22, 5), disposition: 0, gameTime: 21 * 3600,
                grieving: true, alarmed: true, outdoors: true, weatherScene: "scene-old", weatherRevision: 3);
            var newerWeather = Snapshot(Mood(22, 5), disposition: 0, gameTime: 21 * 3600,
                grieving: true, alarmed: true, outdoors: true, weatherScene: "scene-a", weatherRevision: 2);
            Check.True(!Format(staleWeather, "ForSocialReply").Contains("NPC weather:"),
                "Weather from another NPC scene must not modify persistent mood context");
            Check.True(!Format(newerWeather, "ForSocialReply").Contains("NPC weather:"),
                "Weather from a prior NPC scene revision must not modify persistent mood context");
        }

        private static ConversationContextSnapshot Snapshot(object mood, int disposition, double gameTime,
            bool grieving, bool alarmed, bool outdoors, string weatherScene, long weatherRevision,
            string personId = "resident:42")
        {
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, personId,
                personId == null ? "speaker-1" : null,
                "session-a", 1, 7, "0123456789abcdef0123456789abcdef",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "scene-a", 3);
            var weather = ConversationContextFact<ConversationNpcWeatherObservation>.Verified(
                new ConversationNpcWeatherObservation("fog", weatherScene, weatherRevision),
                ConversationContextFactSource.NpcSceneObservation, gameTime);
            var personal = new ConversationContextPersonalState(
                ConversationContextFact<string>.Verified("Breton", ConversationContextFactSource.Save, gameTime),
                ConversationContextFact<string>.Verified("Female", ConversationContextFactSource.Save, gameTime),
                ConversationContextFact<bool>.Verified(true, ConversationContextFactSource.Save, gameTime),
                ConversationContextFact<string>.Verified("Ada", ConversationContextFactSource.Save, gameTime),
                ConversationContextFact<bool>.Verified(grieving, ConversationContextFactSource.Save, gameTime),
                ConversationContextFact<bool>.Verified(alarmed, ConversationContextFactSource.Save, gameTime));
            var snapshotType = typeof(ConversationContextSnapshot);
            var constructor = snapshotType.GetConstructor(new[]
            {
                typeof(ConversationContextBinding), typeof(double), typeof(ConversationContextFact<string>),
                typeof(ConversationContextFact<string>), typeof(ConversationContextFact<ConversationDialogueRole>),
                typeof(ConversationContextFact<string>), typeof(ConversationContextFact<string>),
                typeof(ConversationContextFact<ConversationNpcPosition>), typeof(ConversationContextFact<ConversationNpcActivity>),
                typeof(ConversationContextFact<bool>), typeof(ConversationContextFact<ConversationNpcWeatherObservation>),
                typeof(ConversationContextFact<string>), typeof(int), typeof(uint), typeof(ConversationLocationSnapshot),
                typeof(IEnumerable<ConversationContextCommitment>), typeof(IEnumerable<ConversationContextSourceHandle>),
                typeof(ConversationContextPersonalState), MoodType()
            });
            Check.True(constructor != null, "Snapshot must accept immutable persistent mood projection");
            var name = ConversationContextFact<string>.Verified("Michell Farr", ConversationContextFactSource.NativeActor, gameTime);
            var role = ConversationContextFact<ConversationDialogueRole>.Verified(
                ConversationDialogueRole.ManagedResident, ConversationContextFactSource.NativeActor, gameTime);
            var outdoorFact = ConversationContextFact<bool>.Verified(outdoors, ConversationContextFactSource.LiveObservation, gameTime);
            var traits = ConversationContextFact<string>.Verified("reserved, plainspoken", ConversationContextFactSource.Derived, gameTime);
            var clock = ConversationContextFact<string>.Verified("10:00 PM, 3 Midyear 405", ConversationContextFactSource.LiveObservation, gameTime);
            var playerScene = ConversationContextFact<string>.Verified("Exterior", ConversationContextFactSource.LiveObservation, gameTime);
            var npcScene = ConversationContextFact<string>.Verified("scene-a", ConversationContextFactSource.LiveObservation, gameTime);
            var npcPosition = ConversationContextFact<ConversationNpcPosition>.Verified(
                new ConversationNpcPosition(1, 2, 3), ConversationContextFactSource.LiveObservation, gameTime);
            var activity = ConversationContextFact<ConversationNpcActivity>.Verified(
                ConversationNpcActivity.Stationary, ConversationContextFactSource.LiveObservation, gameTime);
            try
            {
                return (ConversationContextSnapshot)constructor.Invoke(new object[]
                {
                    binding, gameTime, clock, name, role, playerScene, npcScene, npcPosition, activity, outdoorFact,
                    weather, traits, disposition, 123u, null,
                    Array.Empty<ConversationContextCommitment>(), Array.Empty<ConversationContextSourceHandle>(), personal, mood
                });
            }
            catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
        }

        private static string Format(ConversationContextSnapshot snapshot, string projection)
        {
            MethodInfo method = typeof(ConversationContextFormatter).GetMethod(projection, PublicStatic, null,
                new[] { typeof(ConversationContextSnapshot), typeof(int) }, null);
            Check.True(method != null, "Missing formatter projection " + projection);
            return (string)method.Invoke(null, new object[] { snapshot, 4096 });
        }
    }
}
