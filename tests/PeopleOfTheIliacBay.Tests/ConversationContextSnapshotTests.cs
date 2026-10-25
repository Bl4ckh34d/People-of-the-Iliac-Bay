using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ConversationContextSnapshotTests
    {
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
        private static Assembly Core => typeof(WorldStateStore).Assembly;

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Traveler lodging identity is captured once alongside observed position and active action",()=>{
                var empty=ConversationContextPersonalState.Unknown();
                const string identity="You are a mage traveler. Your paid room is at Actual Inn. No permanent house is verified.";
                var fact=ConversationContextFact<string>.Verified(identity,ConversationContextFactSource.Save,100);
                var personal=new ConversationContextPersonalState(empty.Race,empty.Gender,empty.FamiliarWithPlayer,
                    empty.KnownPlayerName,empty.Grieving,empty.Alarmed,null,null,fact);
                var snapshot=(ConversationContextSnapshot)NewSnapshot(NewBinding("Mobile","traveler:42"),15,null,
                    npcOutdoors:true,knownNpcPhysical:true,personalState:personal);
                string context=ConversationContextFormatter.ForSocialReply(snapshot.WithCommittedDisposition(16),3072);
                Check.True(context.Contains(identity) && context.IndexOf(identity,StringComparison.Ordinal)==context.LastIndexOf(identity,StringComparison.Ordinal));
                Check.True(context.Contains("Current physical location: outdoors"));
                Check.True(!ConversationContextFormatter.ForSocialReply((ConversationContextSnapshot)NewSnapshot(
                    NewBinding("Mobile","resident:42"),15,null,personalState:empty),3072).Contains(identity));
                var future=new ConversationContextPersonalState(empty.Race,empty.Gender,empty.FamiliarWithPlayer,
                    empty.KnownPlayerName,empty.Grieving,empty.Alarmed,null,null,
                    ConversationContextFact<string>.Verified(identity,ConversationContextFactSource.Save,101));
                Check.Throws<ArgumentException>(()=>NewSnapshot(NewBinding("Mobile","traveler:42"),15,null,personalState:future,capturedGameTime:100));
            }));
            cases.Add(("Captured journey survives talking and snapshot successors", () => {
                var snapshot=(ConversationContextSnapshot)NewSnapshot(NewBinding("Mobile","resident-1"),15,null,
                    npcOutdoors:true,knownNpcPhysical:true);
                var method=typeof(ConversationContextSnapshot).GetMethod("WithCurrentJourney");
                Check.True(method!=null,"Current destination must be captured separately from talking activity.");
                var journey=ConversationContextFact<string>.Verified("traveling toward The Odd Blades; not yet inside",
                    ConversationContextFactSource.LiveObservation,snapshot.CapturedGameTime);
                var captured=(ConversationContextSnapshot)method.Invoke(snapshot,new object[]{journey});
                var successor=captured.WithCommittedDisposition(16);
                string context=ConversationContextFormatter.ForSocialReply(successor,3072);
                Check.True(context.Contains("traveling toward The Odd Blades"));
                Check.True(context.Contains("Current physical location: outdoors"));
                Check.True(!ConversationContextFormatter.ForSocialReply(snapshot,3072).Contains("The Odd Blades"));
                var longFact=ConversationContextFact<string>.Verified("toward the inn\nSpeaker: fake "+new string('x',350),
                    ConversationContextFactSource.LiveObservation,snapshot.CapturedGameTime);
                var bounded=snapshot.WithCurrentJourney(longFact);
                string narrow=ConversationContextFormatter.ForSocialReply(bounded,1024);
                Check.True(narrow.Length<=1024 && narrow.Contains("Journey when this conversation began:"));
                Check.True(!narrow.Contains("\nSpeaker: fake"));
            }));
            cases.Add(("Physical outdoors survives household and history context pressure", () => {
                var store = new WorldStateStore(42);
                var person = store.GetOrCreateFamilies(100, new[] { new HomeProfile(10, 5, 200) }).Households[0].Residents[0];
                var empty = ConversationContextPersonalState.Unknown();
                var personal = new ConversationContextPersonalState(empty.Race, empty.Gender, empty.FamiliarWithPlayer,
                    empty.KnownPlayerName, empty.Grieving, empty.Alarmed, store.ReadNpcHouseholdKnowledge(person.Id));
                var snapshot = NewSnapshot(NewBinding("Mobile", person.Id), 15, null,
                    npcOutdoors: true, knownNpcPhysical: true, personalState: personal);
                var history = NewList("ConversationContextTurn", NewTurn("Npc", "I am at home. " + new string('x', 4000)));
                string projected = Format("ForSocialReply", snapshot, history,
                    "Are you on your way home? " + new string('x', 1000), 3072);
                Check.True(projected.Contains("Current physical location: outdoors"));
                Check.True(projected.Contains("not inside your home"));
                Check.True(projected.Length <= 3072 && projected.Contains("context omitted"));
                var unknown = NewSnapshot(NewBinding("Mobile", person.Id), 15, null, personalState: personal);
                Check.True(!FormatFacts("ForSocialReply", unknown, 3072).Contains("Current physical location: outdoors"));
                var indoors = NewSnapshot(NewBinding("Mobile", person.Id), 15, null,
                    npcOutdoors: false, knownNpcPhysical: true, personalState: personal);
                Check.True(!FormatFacts("ForSocialReply", indoors, 3072).Contains("not inside your home"));
            }));
            cases.Add(("Social snapshot preserves observed body status without inferring health from unknown facts", BodyStatusProjection));
            cases.Add(("Household projection binds the speaker and preserves captured self knowledge", () => {
                var store = new WorldStateStore(42);
                var person = store.GetOrCreateFamilies(100, new[] { new HomeProfile(10, 5, 200) }).Households[0].Residents[0];
                var empty = ConversationContextPersonalState.Unknown();
                var personal = new ConversationContextPersonalState(empty.Race, empty.Gender, empty.FamiliarWithPlayer,
                    empty.KnownPlayerName, empty.Grieving, empty.Alarmed, store.ReadNpcHouseholdKnowledge(person.Id));
                var snapshot = NewSnapshot(NewBinding("Mobile", person.Id), 15, null, personalState: personal);
                string projected = FormatFacts("ForSocialReply", snapshot, 3072);
                Check.True(projected.Contains(personal.HouseholdKnowledge.ToContext()));
                Check.Throws<ArgumentException>(() => NewSnapshot(NewBinding("Mobile", "someone-else"), 15, null, personalState: personal));
            }));
            cases.Add(("Context actor category stays independent from stable or ephemeral identity", ActorIdentity));
            cases.Add(("Context request binding rejects stale owners but allows same-branch head advance", BindingFreshness));
            cases.Add(("Context facts preserve unknowns and reject mutable reference values", FactImmutability));
            cases.Add(("Typed action commitments are limited to action-task records", ActionCommitments));
            cases.Add(("Snapshot personal state is immutable, unknown-aware, and drives only captured mood", PersonalStateProjection));
            cases.Add(("Familiarity alone does not identify the player by name", FamiliarityWithoutNameProjection));
            cases.Add(("Context snapshot separates player and NPC observations and copies inputs", SnapshotDetaches));
            cases.Add(("Committed disposition successor binds exact parent and preserves parent", SuccessorLineage));
            cases.Add(("Context projections are purpose-specific, attributed, weather-safe, and bounded", FormatterContract));
            cases.Add(("Archive checkpoint accessor returns a detached namespace and ancestry pair", ArchiveCheckpointCopy));
        }

        private static void BodyStatusProjection()
        {
            var type = typeof(ConversationContextPersonalState);
            var fact = typeof(ConversationContextFact<string>);
            var boolean = typeof(ConversationContextFact<bool>);
            var constructor = type.GetConstructor(new[] { fact, fact, boolean, fact, boolean, boolean,
                typeof(NpcHouseholdKnowledge), fact });
            Check.True(constructor != null, "Personal snapshot must capture verified native body status.");
            var empty = ConversationContextPersonalState.Unknown();
            const string status = "Current native status: no active poisoning or disease.";
            var observed = ConversationContextFact<string>.Verified(status, ConversationContextFactSource.NativeActor, 100);
            var personal = constructor.Invoke(new object[] { empty.Race, empty.Gender, empty.FamiliarWithPlayer,
                empty.KnownPlayerName, empty.Grieving, empty.Alarmed, null, observed });
            var snapshot = NewSnapshot(NewBinding("Mobile", "resident:42"), 15, null, personalState: personal);
            Check.True(FormatFacts("ForSocialReply", snapshot, 3072).Contains(status));
            var successor = snapshot.GetType().GetMethod("WithCommittedDisposition", PublicInstance)
                .Invoke(snapshot, new object[] { 70 });
            Check.True(FormatFacts("ForSocialReply", successor, 3072).Contains(status),
                "Disposition changes must preserve the captured body facts.");
            var unknown = NewSnapshot(NewBinding("Mobile", "resident:42"), 15, null, personalState: empty);
            Check.True(!FormatFacts("ForSocialReply", unknown, 3072).Contains(status),
                "An unobserved native condition is not proof of health.");
            var future = constructor.Invoke(new object[] { empty.Race, empty.Gender, empty.FamiliarWithPlayer,
                empty.KnownPlayerName, empty.Grieving, empty.Alarmed, null,
                ConversationContextFact<string>.Verified(status, ConversationContextFactSource.NativeActor, 101) });
            Check.Throws<ArgumentException>(() => NewSnapshot(NewBinding("Mobile", "resident:42"),
                15, null, personalState: future, capturedGameTime: 100));
        }

        private static Type Required(string name)
        {
            var type = Core.GetType("PeopleOfTheIliacBay.Core." + name, false);
            Check.True(type != null, "Missing IMM-02 Core type PeopleOfTheIliacBay.Core." + name);
            return type;
        }

        private static Type FactType(Type valueType = null)
        {
            return Required("ConversationContextFact" + ((char)96) + "1").MakeGenericType(valueType ?? typeof(string));
        }

        private static object NewEnum(string typeName, string value)
        {
            return Enum.Parse(Required(typeName), value);
        }

        private static object NewBinding(string category, string personId = null, string ephemeral = null,
            string session = "session-a", long request = 1, long epoch = 7,
            string archive = "0123456789abcdef0123456789abcdef", string head = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            string scene = "scene-a", long sceneRevision = 3)
        {
            var categoryType = Required("ConversationActorCategory");
            var bindingType = Required("ConversationContextBinding");
            var constructor = bindingType.GetConstructor(new[] { categoryType, typeof(string), typeof(string),
                typeof(string), typeof(long), typeof(long), typeof(string), typeof(string), typeof(string), typeof(long) });
            var hasSceneBinding = constructor != null;
            if (!hasSceneBinding)
                constructor = bindingType.GetConstructor(new[] { categoryType, typeof(string), typeof(string),
                    typeof(string), typeof(long), typeof(long), typeof(string), typeof(string) });
            Check.True(constructor != null, "ConversationContextBinding constructor signature changed");
            try
            {
                var args = new object[] { Enum.Parse(categoryType, category), personId, ephemeral, session,
                    request, epoch, archive, head, scene, sceneRevision };
                return constructor.Invoke(hasSceneBinding ? args : args.Take(8).ToArray());
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            {
                throw error.InnerException;
            }
        }

        private static object NewFact(Type valueType, string factory, object value = null,
            string source = "Save", double observedAt = 100)
        {
            var type = FactType(valueType);
            var method = factory == "Unknown"
                ? type.GetMethod("Unknown", PublicStatic, null, Type.EmptyTypes, null)
                : type.GetMethod("Verified", PublicStatic, null,
                    new[] { valueType, Required("ConversationContextFactSource"), typeof(double) }, null);
            Check.True(method != null, "Missing ConversationContextFact factory " + factory);
            if (factory == "Unknown") return method.Invoke(null, null);
            try { return method.Invoke(null, new[] { value, NewEnum("ConversationContextFactSource", source), (object)observedAt }); }
            catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
        }

        private static object NewStringFact(string value, string source = "Save", double observedAt = 100)
            => value == null ? NewFact(typeof(string), "Unknown") : NewFact(typeof(string), "Verified", value, source, observedAt);

        private static object NewLocation(out AppointmentPlace originalPlace)
        {
            originalPlace = new AppointmentPlace
            {
                Kind = "world-point", SettlementId = 7, BuildingKey = -1,
                X = 12.25, Y = 4, Z = 91.75, Detail = "submission point"
            };
            var type = Required("ConversationLocationSnapshot");
            return Activator.CreateInstance(type, new object[] { 100d, originalPlace });
        }

        private static IList NewList(string typeName, params object[] values)
        {
            var valueType = Required(typeName);
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(valueType));
            foreach (var value in values) list.Add(value);
            return list;
        }

        private static object NewCommitment(string id = "commit-1", string status = "planned", long revision = 2,
            string kind = "shared-activity", string actionKind = null)
        {
            var type = Required("ConversationContextCommitment");
            var ctor = actionKind == null
                ? type.GetConstructor(new[] { typeof(string), typeof(string), typeof(string), typeof(long) })
                : type.GetConstructor(new[] { typeof(string), typeof(string), typeof(string), typeof(long), typeof(NpcActionKind?) });
            Check.True(ctor != null, "Missing immutable commitment constructor");
            try
            {
                return actionKind == null
                    ? ctor.Invoke(new object[] { id, kind, status, revision })
                    : ctor.Invoke(new object[] { id, kind, status, revision, Enum.Parse(typeof(NpcActionKind), actionKind) });
            }
            catch (TargetInvocationException error) when (error.InnerException != null)
            {
                throw error.InnerException;
            }
        }

        private static object NewSourceHandle(string owner = "npc:stable-1", long revision = 3)
        {
            var type = Required("ConversationContextSourceHandle");
            var ctor = type.GetConstructor(new[] { typeof(string), typeof(string), typeof(long) });
            Check.True(ctor != null, "Missing immutable source-handle constructor");
            return ctor.Invoke(new object[] { owner, "inventory", revision });
        }

        private static object NewSnapshot(object binding, int disposition, object location,
            IList commitments = null, IList sources = null, bool knownRole = true,
            bool? npcOutdoors = null, bool knownNpcPhysical = false, string speakerName = "Michell Farr",
            double npcObservedAt = 100, object personalState = null, double capturedGameTime = 100,
            bool knownPersonality = true)
        {
            commitments ??= NewList("ConversationContextCommitment", NewCommitment());
            sources ??= NewList("ConversationContextSourceHandle", NewSourceHandle());
            var snapshotType = Required("ConversationContextSnapshot");
            var roleType = Required("ConversationDialogueRole");
            var positionType = Required("ConversationNpcPosition");
            var activityType = Required("ConversationNpcActivity");
            var weatherType = Required("ConversationNpcWeatherObservation");
            var argsTypes = new[]
            {
                Required("ConversationContextBinding"), typeof(double), FactType(),
                FactType(), FactType(roleType), FactType(), FactType(), FactType(positionType),
                FactType(activityType), FactType(typeof(bool)), FactType(weatherType), FactType(),
                typeof(int), typeof(uint), Required("ConversationLocationSnapshot"),
                typeof(IEnumerable<>).MakeGenericType(Required("ConversationContextCommitment")),
                typeof(IEnumerable<>).MakeGenericType(Required("ConversationContextSourceHandle"))
            };
            if (personalState != null) argsTypes = argsTypes.Concat(new[] { Required("ConversationContextPersonalState") }).ToArray();
            var ctor = snapshotType.GetConstructor(argsTypes);
            Check.True(ctor != null, "ConversationContextSnapshot constructor signature changed");
            var role = knownRole
                ? NewFact(roleType, "Verified", NewEnum("ConversationDialogueRole", "ManagedResident"), "NativeActor")
                : NewFact(roleType, "Unknown");
            var position = knownNpcPhysical
                ? NewFact(positionType, "Verified", Activator.CreateInstance(positionType, new object[] { 1.0, 2.0, 3.0 }), "LiveObservation", npcObservedAt)
                : NewFact(positionType, "Unknown");
            var npcScene = knownNpcPhysical
                ? NewStringFact("scene-a", "LiveObservation", npcObservedAt)
                : NewStringFact(null);
            var npcActivity = knownNpcPhysical
                ? NewFact(activityType, "Verified", NewEnum("ConversationNpcActivity", "Stationary"), "LiveObservation", npcObservedAt)
                : NewFact(activityType, "Unknown");
            var outdoors = npcOutdoors.HasValue
                ? NewFact(typeof(bool), "Verified", npcOutdoors.Value, "LiveObservation", npcObservedAt)
                : NewFact(typeof(bool), "Unknown");
            var weatherValue = Activator.CreateInstance(weatherType, new object[] { "fog", "scene-a", (object)3L });
            var weather = NewFact(weatherType, "Verified", weatherValue, "NpcSceneObservation", npcObservedAt);
            try
            {
                return ctor.Invoke(new[]
                {
                    binding, capturedGameTime, NewStringFact("10:00 AM, 3 Midyear 405"), NewStringFact(speakerName), role,
                    NewStringFact("Exterior", "LiveObservation", npcObservedAt), npcScene, position, npcActivity,
                    outdoors, weather, knownPersonality ? NewStringFact("reserved, plainspoken") : NewStringFact(null),
                    disposition, (object)123u, location, commitments, sources
                }.Concat(personalState == null ? Array.Empty<object>() : new[] { personalState }).ToArray());
            }
            catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
        }

        private static void ActorIdentity()
        {
            var managedStable = NewBinding("Mobile", "resident:42");
            var staticStable = NewBinding("Static", "npc:map:building:hash");
            var guardEphemeral = NewBinding("Guard", null, "guard-speaker-instance-17");
            var unmanagedEphemeral = NewBinding("NativeMobile", null, "speaker-instance-18");
            Check.True(managedStable != null && staticStable != null && guardEphemeral != null && unmanagedEphemeral != null);
            var bindingType = Required("ConversationContextBinding");
            foreach (var property in bindingType.GetProperties(PublicInstance))
                Check.True(!property.CanWrite, "Binding property must be immutable: " + property.Name);
            Check.True(bindingType.GetProperty("SceneId", PublicInstance) != null && bindingType.GetProperty("SceneRevision", PublicInstance) != null,
                "Request binding must capture scene identity and revision");
            Check.Equal("Guard", bindingType.GetProperty("ActorCategory", PublicInstance).GetValue(guardEphemeral).ToString());
            Check.Equal("EphemeralSpeaker", bindingType.GetProperty("IdentityKind", PublicInstance).GetValue(guardEphemeral).ToString());
            Check.Throws<ArgumentException>(() => NewBinding("Mobile"));
            Check.Throws<ArgumentException>(() => NewBinding("Mobile", "resident:42", "speaker-instance"));
            Check.Throws<ArgumentException>(() => NewBinding("Mobile", null, null));
        }

        private static void BindingFreshness()
        {
            var method = Required("ConversationContextBinding").GetMethod("HasSameRequestBinding", PublicInstance);
            Check.True(method != null, "Missing HasSameRequestBinding");
            var captured = NewBinding("Mobile", "resident:42");
            bool Matches(object other) => (bool)method.Invoke(captured, new[] { other });
            Check.True(Matches(NewBinding("Mobile", "resident:42", null, "session-a", 1, 7,
                "0123456789abcdef0123456789abcdef", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")),
                "Same-branch head advance must keep request binding current");
            Check.True(!Matches(NewBinding("Mobile", "resident:99")));
            Check.True(!Matches(NewBinding("Static", "resident:42")));
            Check.True(!Matches(NewBinding("Mobile", "resident:42", null, "session-b")));
            Check.True(!Matches(NewBinding("Mobile", "resident:42", null, "session-a", 2)));
            Check.True(!Matches(NewBinding("Mobile", "resident:42", null, "session-a", 1, 8,
                "0123456789abcdef0123456789abcdef",
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")),
                "Restore epoch must reject old work even in same archive namespace");
            Check.True(!Matches(NewBinding("Mobile", "resident:42", null, "session-a", 1, 7,
                "fedcba9876543210fedcba9876543210")),
                "Different archive namespace must reject old work");
            Check.True(!Matches(NewBinding("Mobile", "resident:42", null, "session-a", 1, 7,
                "0123456789abcdef0123456789abcdef", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "scene-b", 3)),
                "Scene identity change must reject old work");
            Check.True(!Matches(NewBinding("Mobile", "resident:42", null, "session-a", 1, 7,
                "0123456789abcdef0123456789abcdef", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "scene-a", 4)),
                "Scene revision change must reject old work");
            var ephemeral = NewBinding("Guard", null, "guard-instance-a");
            var ephemeralMethod = ephemeral.GetType().GetMethod("HasSameRequestBinding", PublicInstance);
            Check.True(!(bool)ephemeralMethod.Invoke(ephemeral, new[] { NewBinding("Guard", null, "guard-instance-b") }));
        }

        private static void FactImmutability()
        {
            var unknown = NewFact(typeof(string), "Unknown");
            var factType = unknown.GetType();
            Check.Equal("Unknown", factType.GetProperty("Status", PublicInstance).GetValue(unknown).ToString());
            var tryRead = factType.GetMethod("TryGetVerified", PublicInstance);
            Check.True(tryRead != null, "Missing TryGetVerified");
            var args = new object[] { null };
            Check.Equal(false, (bool)tryRead.Invoke(unknown, args));
            Check.True(args[0] == null);
            var verified = NewFact(typeof(string), "Verified", "managed resident", "NativeActor");
            Check.Equal("Verified", factType.GetProperty("Status", PublicInstance).GetValue(verified).ToString());
            args = new object[] { null };
            Check.Equal(true, (bool)tryRead.Invoke(verified, args));
            Check.Equal("managed resident", (string)args[0]);
            foreach (var property in factType.GetProperties(PublicInstance))
                Check.True(!property.CanWrite, "Fact property must be immutable: " + property.Name);
            var listFactType = FactType(typeof(List<string>));
            var mutableFactory = listFactType.GetMethod("Verified", PublicStatic, null,
                new[] { typeof(List<string>), Required("ConversationContextFactSource"), typeof(double) }, null);
            Check.True(mutableFactory != null, "Missing generic Verified factory");
            try
            {
                mutableFactory.Invoke(null, new object[] { new List<string> { "mutable" },
                    NewEnum("ConversationContextFactSource", "Save"), 100d });
                throw new Exception("Mutable reference fact value was accepted");
            }
            catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { }
            Check.Throws<ArgumentException>(() => NewFact(Required("ConversationDialogueRole"), "Verified",
                NewEnum("ConversationDialogueRole", "Unknown"), "NativeActor"));
            Check.Throws<ArgumentException>(() => NewStringFact("   "));
            Check.Throws<ArgumentException>(() => NewFact(typeof(string), "Verified", "\r\n", "Save"));
            var weatherType = Required("ConversationNpcWeatherObservation");
            var weather = Activator.CreateInstance(weatherType, new object[] { "fog", "scene-a", 3L });
            Check.Throws<ArgumentException>(() => NewFact(weatherType, "Verified", weather, "LiveObservation"));
        }

        private static void ActionCommitments()
        {
            var legacy = NewCommitment();
            Check.True(legacy.GetType().GetProperty("ActionKind", PublicInstance) != null, "Commitment must expose typed ActionKind");
            Check.True(legacy.GetType().GetProperty("ActionKind", PublicInstance).GetValue(legacy) == null,
                "Legacy non-action commitment must not imply an action kind");
            Check.Throws<ArgumentException>(() => NewCommitment("bad-kind", "Executing", 1, "shared-activity", "Wait"));
            var action = NewCommitment("wait", "Executing", 1, "action-task", "Wait");
            Check.Equal("Wait", action.GetType().GetProperty("ActionKind", PublicInstance).GetValue(action).ToString());
        }

        private static object NewPersonalState(string race = null, string gender = null, bool? familiar = null,
            string playerName = null, bool? grieving = null, bool? alarmed = null, double observedAt = 100)
        {
            var type = Required("ConversationContextPersonalState");
            var ctor = type.GetConstructor(new[] { FactType(), FactType(), FactType(typeof(bool)), FactType(),
                FactType(typeof(bool)), FactType(typeof(bool)) });
            Check.True(ctor != null, "Missing immutable personal-state constructor");
            return ctor.Invoke(new object[]
            {
                NewStringFact(race, "NativeActor", observedAt), NewStringFact(gender, "NativeActor", observedAt),
                familiar.HasValue ? NewFact(typeof(bool), "Verified", familiar.Value, "NativeActor", observedAt) : NewFact(typeof(bool), "Unknown"),
                NewStringFact(playerName, "NativeActor", observedAt),
                grieving.HasValue ? NewFact(typeof(bool), "Verified", grieving.Value, "NativeActor", observedAt) : NewFact(typeof(bool), "Unknown"),
                alarmed.HasValue ? NewFact(typeof(bool), "Verified", alarmed.Value, "NativeActor", observedAt) : NewFact(typeof(bool), "Unknown")
            });
        }

        private static void PersonalStateProjection()
        {
            var unknownSnapshot = NewSnapshot(NewBinding("Mobile", "resident:42"), 15, null, knownPersonality: false);
            var stateProperty = unknownSnapshot.GetType().GetProperty("PersonalState", PublicInstance);
            Check.True(stateProperty != null, "Snapshot must expose immutable personal state");
            var unknown = stateProperty.GetValue(unknownSnapshot);
            foreach (string name in new[] { "Race", "Gender", "FamiliarWithPlayer", "KnownPlayerName", "Grieving", "Alarmed" })
            {
                var fact = unknown.GetType().GetProperty(name, PublicInstance).GetValue(unknown);
                Check.True(FactUnknown(fact), "Legacy snapshot must preserve unknown personal fact " + name);
            }
            Check.True(!FormatFacts("ForSocialReply", unknownSnapshot, 1024).Contains("Derived mood from tracked inputs:"),
                "Unknown personality/personal inputs cannot produce a derived mood");
            foreach (var property in unknown.GetType().GetProperties(PublicInstance))
                Check.True(!property.CanWrite, "Personal-state fact must be immutable: " + property.Name);

            var personal = NewPersonalState("Breton", "Female", true, "Ada", false, false);
            double captured = 3 * 86400 + 10 * 3600;
            var snapshot = NewSnapshot(NewBinding("Mobile", "resident:42"), -30, null,
                npcOutdoors: true, knownNpcPhysical: true, personalState: personal, capturedGameTime: captured);
            var social = FormatFacts("ForSocialReply", snapshot, 2048);
            var willingness = FormatFacts("ForSemanticClassifier", snapshot, 2048);
            Check.True(social.Contains("Race: Breton") && social.Contains("Gender: Female"), "Verified race/gender must reach the social reply");
            Check.True(social.Contains("Player familiarity: yes") && social.Contains("You already know the player as Ada"),
                "Known player name is available only with verified familiarity");
            var expectedMood = ConversationLifeContext.Mood(123u, 3, new ConversationMoodInputs
            {
                Disposition = -30, Hour = 10, Grieving = false, Alarmed = false, Outdoors = true,
                Weather = "fog", Familiar = true, Activity = "stationary"
            });
            Check.True(social.Contains(expectedMood.Substring("Current mood: ".Length)), "Social mood must derive only from captured time, activity, NPC weather, flags, and disposition");
            Check.True(willingness.Contains(expectedMood.Substring("Current mood: ".Length)),
                "Willingness projection must reuse the same derived mood from verified snapshot inputs");
            var successor = snapshot.GetType().GetMethod("WithCommittedDisposition", PublicInstance).Invoke(snapshot, new object[] { 70 });
            var successorSocial = FormatFacts("ForSocialReply", successor, 2048);
            var successorWillingness = FormatFacts("ForSemanticClassifier", successor, 2048);
            var successorMood = ConversationLifeContext.Mood(123u, 3, new ConversationMoodInputs
                { Disposition = 70, Hour = 10, Grieving = false, Alarmed = false, Outdoors = true, Weather = "fog", Familiar = true, Activity = "stationary" });
            Check.True(successorSocial.Contains(successorMood.Substring("Current mood: ".Length)));
            Check.True(successorWillingness.Contains(successorMood.Substring("Current mood: ".Length)),
                "Committed disposition successor must rederive willingness mood from same captured facts");
            Check.True(!string.Equals(social, successorSocial, StringComparison.Ordinal), "Disposition successor must rederive mood from captured facts");
            Check.True(ReferenceEquals(personal, stateProperty.GetValue(successor)), "Immutable personal state must survive disposition successor");

            var unfamiliar = NewSnapshot(NewBinding("Mobile", "resident:42"), 15, null,
                npcOutdoors: false, knownNpcPhysical: true, personalState: NewPersonalState("Breton", "Female", false, "Ada", false, false),
                capturedGameTime: captured);
            var unfamiliarSocial = FormatFacts("ForSocialReply", unfamiliar, 2048);
            Check.True(unfamiliarSocial.Contains("Player familiarity: no") && !unfamiliarSocial.Contains("Ada"),
                "A known player name must remain hidden when the verified relationship is unfamiliar");
            var noSeed = NewSnapshot(NewBinding("Mobile", "resident:42"), 15, null,
                npcOutdoors: true, knownNpcPhysical: true, personalState: personal, capturedGameTime: captured, knownPersonality: false);
            Check.True(!FormatFacts("ForSocialReply", noSeed, 2048).Contains("Derived mood from tracked inputs:"),
                "Derived mood must be omitted when snapshot personality seed is not justified by a verified personality fact");
            Check.True(!FormatFacts("ForSemanticClassifier", noSeed, 2048).Contains("Derived mood from tracked inputs:"),
                "Willingness mood must be omitted when snapshot personality seed is not justified");
            Check.Throws<ArgumentException>(() => NewSnapshot(NewBinding("Mobile", "resident:42"), 15, null,
                personalState: NewPersonalState("Breton", "Female", true, "Ada", false, false, observedAt: 101)));
            var unknownOutdoors = NewSnapshot(NewBinding("Mobile", "resident:42"), 15, null,
                personalState: NewPersonalState("Breton", "Female", true, "Ada", null, null), capturedGameTime: captured);
            var unknownOutdoorMood = FormatFacts("ForSocialReply", unknownOutdoors, 2048);
            Check.True(unknownOutdoorMood.Contains("Derived mood from tracked inputs:") &&
                unknownOutdoorMood.Contains("NPC outdoor status is unknown") &&
                unknownOutdoorMood.Contains("grieving/alarmed status is untracked") && !unknownOutdoorMood.Contains("NPC weather:"),
                "Unknown NPC outdoors status must omit only outdoor/weather mood modifiers, not all captured mood");
            var unknownOutdoorWillingness = FormatFacts("ForSemanticClassifier", unknownOutdoors, 2048);
            Check.True(unknownOutdoorWillingness.Contains("Derived mood from tracked inputs:") &&
                unknownOutdoorWillingness.Contains("NPC outdoor status is unknown") &&
                unknownOutdoorWillingness.Contains("grieving/alarmed status is untracked") && !unknownOutdoorWillingness.Contains("NPC weather:"),
                "Willingness mood must preserve the same explicit unknown-input caveats");
            double night = 3 * 86400 + 21 * 3600;
            var unknownFamiliarAtNight = NewSnapshot(NewBinding("Mobile", "resident:42"), 15, null,
                npcOutdoors: true, knownNpcPhysical: true, personalState: NewPersonalState("Breton", "Female", null, null, false, false),
                capturedGameTime: night);
            var nightUnknownFamiliarText = FormatFacts("ForSocialReply", unknownFamiliarAtNight, 2048);
            Check.True(nightUnknownFamiliarText.Contains("familiarity is untracked") &&
                !nightUnknownFamiliarText.Contains("wary of an unfamiliar person"),
                "Unknown familiarity cannot be projected as verified unfamiliarity after dark");
        }

        private static void FamiliarityWithoutNameProjection()
        {
            double captured = 3 * 86400 + 10 * 3600;
            var familiarWithoutName = NewSnapshot(NewBinding("Mobile", "resident:42"), 15, null,
                personalState: NewPersonalState("Breton", "Female", true, null, false, false),
                capturedGameTime: captured);
            string social = FormatFacts("ForSocialReply", familiarWithoutName, 2048);
            Check.True(social.Contains("Player familiarity: yes") &&
                !social.Contains("You already know the player as"),
                "Repeated-encounter familiarity without a recorded name must not identify the player by name");
        }

        private static void SnapshotDetaches()
        {
            var location = NewLocation(out var place);
            var commitments = NewList("ConversationContextCommitment", NewCommitment());
            var sources = NewList("ConversationContextSourceHandle", NewSourceHandle());
            var snapshot = NewSnapshot(NewBinding("Mobile", "resident:42"), -5, location, commitments, sources, knownRole: false);
            place.X = 999;
            commitments.Clear();
            sources.Clear();
            var type = snapshot.GetType();
            Check.Equal("Exterior", (string)FactValue(type.GetProperty("PlayerScene", PublicInstance).GetValue(snapshot)));
            Check.True(FactUnknown(type.GetProperty("Role", PublicInstance).GetValue(snapshot)));
            Check.True(FactUnknown(type.GetProperty("NpcScene", PublicInstance).GetValue(snapshot)));
            Check.True(FactUnknown(type.GetProperty("NpcPosition", PublicInstance).GetValue(snapshot)));
            Check.True(FactUnknown(type.GetProperty("NpcActivity", PublicInstance).GetValue(snapshot)));
            Check.True(FactUnknown(type.GetProperty("NpcOutdoors", PublicInstance).GetValue(snapshot)));
            var ownedCommitments = (ICollection)type.GetProperty("Commitments", PublicInstance).GetValue(snapshot);
            var ownedSources = (ICollection)type.GetProperty("ReadSources", PublicInstance).GetValue(snapshot);
            Check.Equal(1, ownedCommitments.Count);
            Check.Equal(1, ownedSources.Count);
            Check.True(((IList)ownedCommitments).IsReadOnly && ((IList)ownedSources).IsReadOnly);
            var frozenLocation = type.GetProperty("PlayerLocationAtSubmission", PublicInstance).GetValue(snapshot);
            var placeCopy = (AppointmentPlace)frozenLocation.GetType().GetMethod("PlaceCopy", PublicInstance).Invoke(frozenLocation, null);
            Check.Equal(12.25, placeCopy.X);
            placeCopy.X = 200;
            var secondCopy = (AppointmentPlace)frozenLocation.GetType().GetMethod("PlaceCopy", PublicInstance).Invoke(frozenLocation, null);
            Check.Equal(12.25, secondCopy.X);
            var withoutHere = NewSnapshot(NewBinding("Mobile", "resident:42"), -5, null);
            Check.True(withoutHere.GetType().GetProperty("PlayerLocationAtSubmission", PublicInstance).GetValue(withoutHere) == null,
                "Missing HERE must remain unknown rather than requiring or inventing a point");
            var unknownHere = Format("ForSocialReply", withoutHere, NewList("ConversationContextTurn"), "Hello.", 512);
            Check.True(unknownHere.Contains("Player appointment HERE at submission: unknown"));
            Check.Throws<ArgumentException>(() => NewSnapshot(NewBinding("Mobile", "resident:42"), -5, location,
                npcOutdoors: true, knownNpcPhysical: true, npcObservedAt: 101));
        }

        private static bool FactUnknown(object fact)
            => fact.GetType().GetProperty("Status", PublicInstance).GetValue(fact).ToString() == "Unknown";

        private static string FactValue(object fact)
        {
            var method = fact.GetType().GetMethod("TryGetVerified", PublicInstance);
            var args = new object[] { null };
            return (bool)method.Invoke(fact, args) ? args[0].ToString() : null;
        }

        private static void SuccessorLineage()
        {
            var location = NewLocation(out _);
            var binding = NewBinding("Mobile", "resident:42");
            var parent = NewSnapshot(binding, -5, location);
            var sibling = NewSnapshot(binding, -5, location);
            var type = parent.GetType();
            Check.Equal(0L, Convert.ToInt64(type.GetProperty("Revision", PublicInstance).GetValue(parent)));
            var create = type.GetMethod("WithCommittedDisposition", PublicInstance);
            Check.True(create != null, "Missing immutable committed-disposition successor");
            var successor = create.Invoke(parent, new object[] { 15 });
            Check.Equal(-5, (int)type.GetProperty("EffectiveDisposition", PublicInstance).GetValue(parent));
            Check.Equal(15, (int)type.GetProperty("EffectiveDisposition", PublicInstance).GetValue(successor));
            Check.Equal(1L, Convert.ToInt64(type.GetProperty("Revision", PublicInstance).GetValue(successor)));
            Check.True((bool)type.GetMethod("IsSuccessorOf", PublicInstance).Invoke(successor, new[] { parent }));
            Check.True(!(bool)type.GetMethod("IsSuccessorOf", PublicInstance).Invoke(successor, new[] { sibling }),
                "Successor must identify exact parent, not only request/revision");
            try
            {
                create.Invoke(parent, new object[] { -5 });
                throw new Exception("No-op disposition transition created successor");
            }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
        }

        private static object NewTurn(string speaker, string text)
        {
            var speakerType = Required("ConversationTextSpeaker");
            var turnType = Required("ConversationContextTurn");
            var ctor = turnType.GetConstructor(new[] { speakerType, typeof(string) });
            Check.True(ctor != null, "Missing immutable attributed-history turn constructor");
            return ctor.Invoke(new[] { Enum.Parse(speakerType, speaker), text });
        }

        private static string Format(string method, object snapshot, IList turns, string current, int maxChars)
        {
            var formatter = Required("ConversationContextFormatter");
            var turnType = Required("ConversationContextTurn");
            var enumerable = typeof(IEnumerable<>).MakeGenericType(turnType);
            var methodInfo = formatter.GetMethod(method, PublicStatic, null,
                new[] { Required("ConversationContextSnapshot"), enumerable, typeof(string), typeof(int) }, null);
            Check.True(methodInfo != null, "Missing formatter projection " + method);
            return (string)methodInfo.Invoke(null, new object[] { snapshot, turns, current, maxChars });
        }

        private static string FormatFacts(string method, object snapshot, int maxChars)
        {
            var formatter = Required("ConversationContextFormatter");
            var methodInfo = formatter.GetMethod(method, PublicStatic, null,
                new[] { Required("ConversationContextSnapshot"), typeof(int) }, null);
            Check.True(methodInfo != null, "Missing facts-only formatter projection " + method);
            return (string)methodInfo.Invoke(null, new object[] { snapshot, maxChars });
        }

        private static void FormatterContract()
        {
            var location = NewLocation(out _);
            var snapshot = NewSnapshot(NewBinding("Mobile", "resident:42"), 15, location,
                commitments: NewList("ConversationContextCommitment", NewCommitment(),
                    NewCommitment("wait-1", "Executing", 5, "action-task", "Wait"),
                    NewCommitment("task-ready", "Ready", 6, "task"),
                    NewCommitment("task-running", "Running", 7, "task"),
                    NewCommitment("task-completed", "Completed", 7, "task"),
                    NewCommitment("appointment-waiting", "Waiting", 8, "appointment"),
                    NewCommitment("appointment-attended", "Attended", 9, "appointment"),
                    NewCommitment("bad-shared-waiting", "Waiting", 10, "shared-activity"),
                    NewCommitment("secret", "hidden", 4, "quest-secret")), npcOutdoors: null, knownNpcPhysical: false,
                speakerName: "Michell\nFarr");
            var history = NewList("ConversationContextTurn", NewTurn("Npc", "Quiet day here."),
                NewTurn("Player", "I visited the harbor."));
            const string current = "Please tell me about yourself.";
            var routing = Format("ForRouting", snapshot, history, current, 1024);
            var semantic = Format("ForSemanticClassifier", snapshot, history, current, 1024);
            var social = Format("ForSocialReply", snapshot, history, current, 1024);
            var routingFacts = FormatFacts("ForRouting", snapshot, 1024);
            var semanticFacts = FormatFacts("ForSemanticClassifier", snapshot, 1024);
            var socialFacts = FormatFacts("ForSocialReply", snapshot, 1024);
            Check.True(routing.Contains("Player: I visited the harbor.") && routing.Contains("NPC: Quiet day here."), "Routing lost attributed history");
            Check.True(routing.LastIndexOf(current, StringComparison.Ordinal) > routing.LastIndexOf("Player: I visited the harbor.", StringComparison.Ordinal), "Current message must follow history");
            Check.True(semantic.Contains("Current shared activity: planned"), "Semantic projection omitted safe commitments");
            Check.True(routing.Contains("Current action: Wait (executing)"), "Routing must know a native Wait action is currently executing");
            Check.True(routing.Contains("Current shared activity: planned"), "Routing must retain safe current shared activities");
            Check.True(routing.Contains("Current task: ready") && routing.Contains("Current task: running"),
                "Routing must preserve the captured ready/running task states");
            Check.True(routing.Contains("Current appointment: waiting") && routing.Contains("Current appointment: attended (in progress)"),
                "Routing must preserve waiting and attended appointments without describing attendance as a future plan");
            Check.True(!routing.Contains("Previous task: completed") && semantic.Contains("Previous task: completed"),
                "A completed task is historical for social/semantic context and never a current routing task");
            Check.True(!routing.Contains("Current shared activity: waiting"),
                "Statuses are validated against their own source commitment kind");
            Check.True(routingFacts.Contains("Current action: Wait (executing)") && semanticFacts.Contains("Current action: Wait (executing)"),
                "Facts-only routing and semantic projections must retain current action commitments");
            Check.True(socialFacts.Contains("Player scene at submission: Exterior"), "Facts-only social projection omitted verified context");
            foreach (var factsOnly in new[] { routingFacts, semanticFacts, socialFacts })
                Check.True(!factsOnly.Contains("Current player message:") && !factsOnly.Contains("Player:") && !factsOnly.Contains("NPC:"),
                    "Facts-only projection must not duplicate transcript turns");
            Check.True(routingFacts.Length <= 1024 && semanticFacts.Length <= 1024 && socialFacts.Length <= 1024);
            Check.True(semantic.Contains("10:00 AM, 3 Midyear 405") && !semantic.Contains("100"), "Semantic projection must use readable captured calendar time, not raw seconds");
            Check.True(social.Contains("Player scene at submission: Exterior"), "Social projection omitted player scene");
            Check.True(social.Contains("Player appointment HERE at submission"), "Social projection omitted fixed HERE");
            Check.True(social.Contains("NPC scene: unknown") && social.Contains("NPC position: unknown") && social.Contains("NPC activity: unknown"), "Unknown NPC observations were not explicit");
            Check.True(!routing.Contains("quest-secret") && !semantic.Contains("quest-secret") && !social.Contains("quest-secret"),
                "Unallowlisted commitment data must not be disclosed in any projection");
            Check.True(!social.Contains("Michell\nFarr") && social.Contains("Michell Farr"), "Multiline fact data must be escaped into one atomic row");
            Check.True(!social.Contains("fog"), "Do not borrow player weather when NPC outdoors status is unknown");
            Check.True(routing.Length <= 1024 && semantic.Length <= 1024 && social.Length <= 1024, "Projection exceeded character budget");
            var outdoorSnapshot = NewSnapshot(NewBinding("Mobile", "resident:42"), 15, location, npcOutdoors: true, knownNpcPhysical: true);
            var outdoor = Format("ForSocialReply", outdoorSnapshot, history, current, 1024);
            Check.True(outdoor.Contains("Current physical location: outdoors"), "Physical location must outrank optional weather at a tight budget");
            Check.True(Format("ForSocialReply", outdoorSnapshot, history, current, 3072).Contains("NPC weather: fog"),
                "Verified outdoor NPC weather should remain when the budget permits");
            var unknownNpcScene = NewSnapshot(NewBinding("Mobile", "resident:42"), 15, location, npcOutdoors: true, knownNpcPhysical: false);
            Check.True(!Format("ForSocialReply", unknownNpcScene, history, current, 1024).Contains("NPC weather:"),
                "Player-area weather cannot fill an unknown NPC scene");
            var staleWeatherScene = NewSnapshot(NewBinding("Mobile", "resident:42", null, "session-a", 1, 7,
                "0123456789abcdef0123456789abcdef", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "scene-a", 4),
                15, location, npcOutdoors: true, knownNpcPhysical: true);
            Check.True(!Format("ForSocialReply", staleWeatherScene, history, current, 1024).Contains("NPC weather:"),
                "Weather from a prior scene revision cannot be projected");
            var tinyHistory = NewList("ConversationContextTurn", NewTurn("Npc", new string('x', 4000)));
            var bounded = Format("ForSocialReply", snapshot, tinyHistory, current, 300);
            Check.True(bounded.Length <= 300 && bounded.EndsWith(current, StringComparison.Ordinal),
                "Formatter may trim prior history but must preserve full current message");
            Check.True(bounded.Contains("Player appointment HERE at submission"), "History fitting must preserve fixed player HERE");
            Check.True(bounded.Contains("context omitted"), "Bounded projection must disclose omitted context");
            try
            {
                Format("ForSocialReply", snapshot, NewList("ConversationContextTurn"), current, current.Length + 1);
                throw new Exception("Impossible projection budget was accepted");
            }
            catch (TargetInvocationException error) when (error.InnerException is ArgumentOutOfRangeException) { }
        }

        private static void ArchiveCheckpointCopy()
        {
            string root = Path.Combine(Path.GetTempPath(), "lc-context-" + Guid.NewGuid().ToString("N"));
            var store = new WorldStateStore(77, root);
            var method = typeof(WorldStateStore).GetMethod("CaptureConversationArchiveCheckpoint", PublicInstance);
            Check.True(method != null, "Missing copied archive checkpoint accessor");
            var copy = method.Invoke(store, null);
            var type = copy.GetType();
            Check.True(type.Name == "ConversationArchiveCheckpointView");
            Check.True(type != typeof(ConversationArchiveCheckpoint), "Accessor must not leak mutable checkpoint");
            foreach (var property in type.GetProperties(PublicInstance))
                Check.True(!property.CanWrite, "Archive view property must be immutable: " + property.Name);
            var saved = store.Snapshot();
            var savedCheckpoint = saved.ConversationArchive;
            string capturedId = (string)type.GetProperty("ArchiveId", PublicInstance).GetValue(copy);
            string capturedHead = (string)type.GetProperty("Head", PublicInstance).GetValue(copy);
            savedCheckpoint.ArchiveId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            savedCheckpoint.Head = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            var second = method.Invoke(store, null);
            Check.Equal(capturedId, (string)type.GetProperty("ArchiveId", PublicInstance).GetValue(second));
            Check.Equal(capturedHead, (string)type.GetProperty("Head", PublicInstance).GetValue(second));
        }
    }
}
