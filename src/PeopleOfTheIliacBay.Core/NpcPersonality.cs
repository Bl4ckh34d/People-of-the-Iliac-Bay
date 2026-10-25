using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PeopleOfTheIliacBay.Tests")]

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcSocialBackground
    {
        Unknown = 0,
        Commoner = 1,
        Noble = 2,
        Merchant = 3,
        Scholar = 4,
        Underworld = 5,
        Supernatural = 6,
        GuildMember = 7
    }

    // Concrete selected values are saved so later generator changes cannot reroll a person.
    [Serializable]
    public sealed class NpcPersonalityProfile
    {
        public string PersonId;
        public int Version = 3;
        public uint Seed;
        public NpcSocialBackground Background;
        public bool RandomTraitsSuppressed;
        public string Warmth;
        public string Patience;
        public string Curiosity;
        public string Privacy;
        public string Manner;
        public string Outlook;
        public string EmotionalStability;
        public string Ambition;
        public string Integrity;
        public string Courage;
        public string Dutifulness;
        public string Vocabulary;
        public string Eloquence;
        public string StatusAttitude;
        public string Humor;
        public string SelfEsteem;
        public string Sensitivity;
        public string Greed, Generosity, Compassion, Forgiveness, Humility;

        public IReadOnlyDictionary<string, string> SelectedTraits
        {
            get
            {
                var traits = new Dictionary<string, string>(Version == 1 ? 14 : 17, StringComparer.Ordinal)
                {
                    { "warmth", Warmth }, { "patience", Patience }, { "curiosity", Curiosity },
                    { "privacy", Privacy }, { "manner", Manner }, { "outlook", Outlook },
                    { "emotional stability", EmotionalStability }, { "ambition", Ambition },
                    { "integrity", Integrity }, { "courage", Courage }, { "dutifulness", Dutifulness },
                    { "vocabulary", Vocabulary }, { "eloquence", Eloquence },
                    { "status attitude", StatusAttitude }
                };
                if (Version != 1)
                {
                    traits.Add("humor", Humor);
                    traits.Add("self esteem", SelfEsteem);
                    traits.Add("sensitivity", Sensitivity);
                }
                if (Version == 3)
                {
                    traits.Add("greed", Greed); traits.Add("generosity", Generosity);
                    traits.Add("compassion", Compassion); traits.Add("forgiveness", Forgiveness); traits.Add("humility", Humility);
                    foreach (string key in new List<string>(traits.Keys)) if (traits[key] == null) traits.Remove(key);
                }
                return new ReadOnlyDictionary<string, string>(traits);
            }
        }

        public static NpcPersonalityProfile CopyValidated(NpcPersonalityProfile source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Validate(source);
            if (source.Version == 3)
            {
                var sparse = new NpcPersonalityProfile { PersonId = source.PersonId, Seed = source.Seed,
                    Background = source.Background, RandomTraitsSuppressed = source.RandomTraitsSuppressed, Version = 3 };
                foreach (var axis in Axes) axis.Set(sparse, axis.Get(source));
                return sparse;
            }
            NpcPersonalityProfile copy = new NpcPersonalityProfile
            {
                PersonId = source.PersonId,
                Version = 2,
                Seed = source.Seed,
                Background = source.Background,
                RandomTraitsSuppressed = source.RandomTraitsSuppressed,
                Warmth = source.Warmth,
                Patience = source.Patience,
                Curiosity = source.Curiosity,
                Privacy = source.Privacy,
                Manner = source.Manner,
                Outlook = source.Outlook,
                EmotionalStability = source.EmotionalStability,
                Ambition = source.Ambition,
                Integrity = source.Integrity,
                Courage = source.Courage,
                Dutifulness = source.Dutifulness,
                Vocabulary = source.Vocabulary,
                Eloquence = source.Eloquence,
                StatusAttitude = source.StatusAttitude
            };
            if (source.Version == 1)
            {
                NpcPersonalityGenerator.BackfillV2Axes(source.PersonId, source.Seed,
                    out copy.Humor, out copy.SelfEsteem, out copy.Sensitivity);
            }
            else
            {
                copy.Humor = source.Humor;
                copy.SelfEsteem = source.SelfEsteem;
                copy.Sensitivity = source.Sensitivity;
            }
            Validate(copy);
            return copy;
        }

        public string Description()
        {
            Validate(this);
            var profile = this;
            if (Version == 3)
            {
                var sparse = new StringBuilder("Stable personality traits (exactly three): ");
                bool first = true;
                foreach (var trait in SelectedTraits)
                { if (!first) sparse.Append("; "); first = false; sparse.Append(trait.Key).Append(' ').Append(trait.Value); }
                if (Vocabulary == "coarse") sparse.Append(" (may use vulgar language)");
                return sparse.Append('.').ToString();
            }
            var text = new StringBuilder("Stable personality traits: warmth ").Append(profile.Warmth)
                .Append("; patience ").Append(profile.Patience)
                .Append("; curiosity ").Append(profile.Curiosity)
                .Append("; privacy ").Append(profile.Privacy)
                .Append("; manner ").Append(profile.Manner)
                .Append("; outlook ").Append(profile.Outlook)
                .Append("; emotional stability ").Append(profile.EmotionalStability)
                .Append("; ambition ").Append(profile.Ambition)
                .Append("; integrity ").Append(profile.Integrity)
                .Append("; courage ").Append(profile.Courage)
                .Append("; dutifulness ").Append(profile.Dutifulness)
                .Append("; vocabulary ").Append(profile.Vocabulary);
            if (profile.Vocabulary == "coarse") text.Append(" (may use vulgar language)");
            text.Append("; eloquence ").Append(profile.Eloquence)
                .Append("; status attitude ").Append(profile.StatusAttitude);
            if (profile.Version == 2)
                text.Append("; humor ").Append(profile.Humor)
                    .Append("; self esteem ").Append(profile.SelfEsteem)
                    .Append("; sensitivity ").Append(profile.Sensitivity);
            text.Append('.');
            if (text.Length > 1100) throw new InvalidOperationException("Personality description exceeds its fixed limit.");
            return text.ToString();
        }

        private static void Validate(NpcPersonalityProfile profile)
        {
            if (profile.Version != 1 && profile.Version != 2 && profile.Version != 3) throw new ArgumentOutOfRangeException(nameof(profile.Version));
            if (string.IsNullOrWhiteSpace(profile.PersonId) || profile.PersonId.Length > 128)
                throw new ArgumentException("PersonId must contain 1 to 128 characters.", nameof(profile.PersonId));
            for (int i = 0; i < profile.PersonId.Length; i++)
                if (char.IsControl(profile.PersonId[i]))
                    throw new ArgumentException("PersonId cannot contain control characters.", nameof(profile.PersonId));
            if (!Enum.IsDefined(typeof(NpcSocialBackground), profile.Background))
                throw new ArgumentOutOfRangeException(nameof(profile.Background));
            if (profile.Version == 3)
            {
                int count = 0;
                foreach (var axis in Axes)
                {
                    string value = axis.Get(profile);
                    if (value == null) continue;
                    Require(value, axis.Values, axis.Name); count++;
                }
                if (count != 3) throw new ArgumentException("An ordinary personality must contain exactly three traits.");
                return;
            }
            if (profile.Greed != null || profile.Generosity != null || profile.Compassion != null ||
                profile.Forgiveness != null || profile.Humility != null)
                throw new ArgumentException("Legacy personality cannot contain version 3 traits.");
            Require(profile.Warmth, WarmthValues, nameof(profile.Warmth));
            Require(profile.Patience, PatienceValues, nameof(profile.Patience));
            Require(profile.Curiosity, CuriosityValues, nameof(profile.Curiosity));
            Require(profile.Privacy, PrivacyValues, nameof(profile.Privacy));
            Require(profile.Manner, MannerValues, nameof(profile.Manner));
            Require(profile.Outlook, OutlookValues, nameof(profile.Outlook));
            Require(profile.EmotionalStability, EmotionalStabilityValues, nameof(profile.EmotionalStability));
            Require(profile.Ambition, AmbitionValues, nameof(profile.Ambition));
            Require(profile.Integrity, IntegrityValues, nameof(profile.Integrity));
            Require(profile.Courage, CourageValues, nameof(profile.Courage));
            Require(profile.Dutifulness, DutifulnessValues, nameof(profile.Dutifulness));
            Require(profile.Vocabulary, VocabularyValues, nameof(profile.Vocabulary));
            Require(profile.Eloquence, EloquenceValues, nameof(profile.Eloquence));
            Require(profile.StatusAttitude, StatusAttitudeValues, nameof(profile.StatusAttitude));
            if (profile.Version == 1)
            {
                if (profile.Humor != null || profile.SelfEsteem != null || profile.Sensitivity != null)
                    throw new ArgumentException("Version 1 personality cannot contain version 2 traits.");
            }
            else
            {
                Require(profile.Humor, HumorValues, nameof(profile.Humor));
                Require(profile.SelfEsteem, SelfEsteemValues, nameof(profile.SelfEsteem));
                Require(profile.Sensitivity, SensitivityValues, nameof(profile.Sensitivity));
            }
        }

        private static void Require(string value, string[] allowed, string name)
        {
            for (int i = 0; i < allowed.Length; i++)
                if (string.Equals(value, allowed[i], StringComparison.Ordinal)) return;
            throw new ArgumentException("Trait value is not in the supported vocabulary.", name);
        }

        internal static readonly string[] WarmthValues = { "warm", "reserved", "guarded", "affectionate" };
        internal static readonly string[] PatienceValues = { "patient", "brisk", "easily wearied", "unhurried" };
        internal static readonly string[] CuriosityValues = { "inquisitive", "practical", "observant", "self-absorbed" };
        internal static readonly string[] PrivacyValues = { "open", "private", "selective" };
        internal static readonly string[] MannerValues = { "plainspoken", "courteous", "dry-humored", "earnest" };
        internal static readonly string[] OutlookValues = { "optimistic", "pragmatic", "pessimistic", "cynical" };
        internal static readonly string[] EmotionalStabilityValues = { "even-tempered", "sensitive", "volatile", "resilient" };
        internal static readonly string[] AmbitionValues = { "content", "aspiring", "driven", "status-seeking" };
        internal static readonly string[] IntegrityValues = { "honest", "deceitful", "self-serving", "principled", "sly", "manipulative" };
        internal static readonly string[] CourageValues = { "cautious", "bold", "reckless", "steadfast" };
        internal static readonly string[] DutifulnessValues = { "independent", "conscientious", "obliging", "self-directed" };
        internal static readonly string[] VocabularyValues = { "plain", "precise", "colorful", "earthy", "coarse" };
        internal static readonly string[] EloquenceValues = { "plain", "direct", "articulate", "eloquent" };
        internal static readonly string[] StatusAttitudeValues = { "egalitarian", "deferential", "status-conscious", "condescending" };
        internal static readonly string[] HumorValues = { "playful", "wry", "solemn", "mischievous" };
        internal static readonly string[] SelfEsteemValues = { "insecure", "grounded", "confident" };
        internal static readonly string[] SensitivityValues = { "thick-skinned", "attuned", "highly sensitive" };
        internal sealed class Axis
        {
            internal readonly string Name;
            internal readonly string[] Values;
            internal readonly Func<NpcPersonalityProfile, string> Get;
            internal readonly Action<NpcPersonalityProfile, string> Set;
            internal Axis(string name, string[] values, Func<NpcPersonalityProfile, string> get,
                Action<NpcPersonalityProfile, string> set) { Name = name; Values = values; Get = get; Set = set; }
        }
        private static string[] Extended(string[] values, string extra)
        { var result = new List<string>(values); result.Add(extra); return result.ToArray(); }
        internal static readonly Axis[] Axes = {
            new Axis("warmth", Extended(WarmthValues, "unfriendly"), p => p.Warmth, (p, v) => p.Warmth = v),
            new Axis("patience", Extended(PatienceValues, "impatient"), p => p.Patience, (p, v) => p.Patience = v),
            new Axis("curiosity", CuriosityValues, p => p.Curiosity, (p, v) => p.Curiosity = v),
            new Axis("privacy", PrivacyValues, p => p.Privacy, (p, v) => p.Privacy = v),
            new Axis("manner", Extended(MannerValues, "rude"), p => p.Manner, (p, v) => p.Manner = v),
            new Axis("outlook", OutlookValues, p => p.Outlook, (p, v) => p.Outlook = v),
            new Axis("emotional stability", EmotionalStabilityValues, p => p.EmotionalStability, (p, v) => p.EmotionalStability = v),
            new Axis("ambition", AmbitionValues, p => p.Ambition, (p, v) => p.Ambition = v),
            new Axis("integrity", IntegrityValues, p => p.Integrity, (p, v) => p.Integrity = v),
            new Axis("courage", Extended(CourageValues, "cowardly"), p => p.Courage, (p, v) => p.Courage = v),
            new Axis("dutifulness", Extended(DutifulnessValues, "irresponsible"), p => p.Dutifulness, (p, v) => p.Dutifulness = v),
            new Axis("vocabulary", VocabularyValues, p => p.Vocabulary, (p, v) => p.Vocabulary = v),
            new Axis("eloquence", EloquenceValues, p => p.Eloquence, (p, v) => p.Eloquence = v),
            new Axis("status attitude", StatusAttitudeValues, p => p.StatusAttitude, (p, v) => p.StatusAttitude = v),
            new Axis("humor", HumorValues, p => p.Humor, (p, v) => p.Humor = v),
            new Axis("self esteem", SelfEsteemValues, p => p.SelfEsteem, (p, v) => p.SelfEsteem = v),
            new Axis("sensitivity", SensitivityValues, p => p.Sensitivity, (p, v) => p.Sensitivity = v),
            new Axis("greed", new[] { "greedy", "unmaterialistic" }, p => p.Greed, (p, v) => p.Greed = v),
            new Axis("generosity", new[] { "generous", "stingy" }, p => p.Generosity, (p, v) => p.Generosity = v),
            new Axis("compassion", new[] { "compassionate", "callous", "cruel" }, p => p.Compassion, (p, v) => p.Compassion = v),
            new Axis("forgiveness", new[] { "forgiving", "vindictive" }, p => p.Forgiveness, (p, v) => p.Forgiveness = v),
            new Axis("humility", new[] { "humble", "arrogant" }, p => p.Humility, (p, v) => p.Humility = v),
        };

        // Preserve a stable subset of the saved values rather than inventing a new past personality.
        public static NpcPersonalityProfile ToThreeTraits(NpcPersonalityProfile source)
        {
            var validated = CopyValidated(source);
            if (source.Version == 3) return validated;
            var candidates = new List<Axis>();
            foreach (var axis in Axes) if (axis.Get(source) != null) candidates.Add(axis);
            var result = new NpcPersonalityProfile { PersonId = source.PersonId, Seed = source.Seed,
                Background = source.Background, RandomTraitsSuppressed = source.RandomTraitsSuppressed, Version = 3 };
            uint identity = NpcPersonalityGenerator.HashPersonId(source.PersonId);
            for (uint i = 0; i < 3; i++)
            {
                int index = (int)(StableSeed.Combine(source.Seed, identity, i, 0x6d696733u) % (uint)candidates.Count);
                Axis axis = candidates[index]; candidates.RemoveAt(index); axis.Set(result, axis.Get(source));
            }
            return CopyValidated(result);
        }

    }

    public static class NpcPersonalityGenerator
    {
        public static NpcPersonalityProfile Generate(string personId, uint seed, NpcSocialBackground background)
        {
            // Validate identity without producing extra hidden traits.
            if (string.IsNullOrWhiteSpace(personId) || personId.Length > 128 || !Enum.IsDefined(typeof(NpcSocialBackground), background))
                throw new ArgumentException("Invalid personality identity or background.");
            foreach (char c in personId) if (char.IsControl(c)) throw new ArgumentException("Invalid personality identity.");
            var result = new NpcPersonalityProfile { PersonId = personId, Seed = seed, Background = background, Version = 3 };
            var pool = new List<KeyValuePair<NpcPersonalityProfile.Axis, string>>();
            foreach (var axis in NpcPersonalityProfile.Axes)
                foreach (string value in axis.Values) pool.Add(new KeyValuePair<NpcPersonalityProfile.Axis, string>(axis, value));
            uint identity = HashPersonId(personId);
            for (uint i = 0; i < 3; i++)
            {
                int index = (int)(StableSeed.Combine(seed, identity, i, 0x74726933u) % (uint)pool.Count);
                var chosen = pool[index]; chosen.Key.Set(result, chosen.Value);
                // Distinct dimensions prevent duplicates and direct opposites, never balance morality.
                pool.RemoveAll(candidate => candidate.Key == chosen.Key);
            }
            return NpcPersonalityProfile.CopyValidated(result);
        }

        // Historical fixtures and compatibility diagnostics only; live generation uses V3 above.
        internal static NpcPersonalityProfile GenerateLegacyV2(string personId, uint seed, NpcSocialBackground background)
        {
            if (string.IsNullOrWhiteSpace(personId) || personId.Length > 128)
                throw new ArgumentException("PersonId must contain 1 to 128 characters.", nameof(personId));
            if (!Enum.IsDefined(typeof(NpcSocialBackground), background))
                throw new ArgumentOutOfRangeException(nameof(background));
            for (int i = 0; i < personId.Length; i++)
                if (char.IsControl(personId[i])) throw new ArgumentException("PersonId cannot contain control characters.", nameof(personId));

            uint identity = HashPersonId(personId);
            return NpcPersonalityProfile.CopyValidated(new NpcPersonalityProfile
            {
                PersonId = personId,
                Version = 2,
                Seed = seed,
                Background = background,
                Warmth = Pick(seed, NpcPersonalityProfile.WarmthValues, 1),
                Patience = Pick(seed, NpcPersonalityProfile.PatienceValues, 2),
                Curiosity = Pick(seed, NpcPersonalityProfile.CuriosityValues, 3),
                Privacy = Pick(seed, NpcPersonalityProfile.PrivacyValues, 4),
                Manner = Pick(seed, NpcPersonalityProfile.MannerValues, 5),
                Outlook = Pick(seed, NpcPersonalityProfile.OutlookValues, identity, 6),
                EmotionalStability = Pick(seed, NpcPersonalityProfile.EmotionalStabilityValues, identity, 7),
                Ambition = Pick(seed, NpcPersonalityProfile.AmbitionValues, identity, 8),
                Integrity = Pick(seed, NpcPersonalityProfile.IntegrityValues, identity, 9),
                Courage = Pick(seed, NpcPersonalityProfile.CourageValues, identity, 10),
                Dutifulness = Pick(seed, NpcPersonalityProfile.DutifulnessValues, identity, 11),
                Vocabulary = PickVocabulary(seed, identity),
                Eloquence = PickEloquence(seed, identity, background),
                StatusAttitude = Pick(seed, NpcPersonalityProfile.StatusAttitudeValues, identity, 14),
                Humor = Pick(seed, NpcPersonalityProfile.HumorValues, identity, 15),
                SelfEsteem = Pick(seed, NpcPersonalityProfile.SelfEsteemValues, identity, 16),
                Sensitivity = Pick(seed, NpcPersonalityProfile.SensitivityValues, identity, 17)
            });
        }

        internal static void BackfillV2Axes(string personId, uint seed, out string humor,
            out string selfEsteem, out string sensitivity)
        {
            uint identity = HashPersonId(personId);
            humor = Pick(seed, NpcPersonalityProfile.HumorValues, identity, 15);
            selfEsteem = Pick(seed, NpcPersonalityProfile.SelfEsteemValues, identity, 16);
            sensitivity = Pick(seed, NpcPersonalityProfile.SensitivityValues, identity, 17);
        }

        private static string Pick(uint seed, string[] values, uint axis)
        { return values[(int)(StableSeed.Combine(seed, 0x70657273u, axis, 0x6f6e616cu) % (uint)values.Length)]; }

        private static string Pick(uint seed, string[] values, uint identity, uint axis)
        { return values[(int)(StableSeed.Combine(seed, identity, axis, 0x70657273u) % (uint)values.Length)]; }

        private static string PickVocabulary(uint seed, uint identity)
        {
            // Coarse language stays uncommon, while eloquence remains independent.
            uint value = StableSeed.Combine(seed, identity, 12, 0x70657273u) % 100;
            if (value < 4) return "coarse";
            if (value < 14) return "earthy";
            return NpcPersonalityProfile.VocabularyValues[(int)(value % 3)];
        }

        private static string PickEloquence(uint seed, uint identity, NpcSocialBackground background)
        {
            uint value = StableSeed.Combine(seed, identity, 13, 0x70657273u) % 100;
            if (background == NpcSocialBackground.Noble)
                return value < 7 ? "plain" : value < 19 ? "direct" : value < 58 ? "articulate" : "eloquent";
            if (background == NpcSocialBackground.Commoner)
                return value < 28 ? "plain" : value < 67 ? "direct" : value < 91 ? "articulate" : "eloquent";
            return value < 20 ? "plain" : value < 45 ? "direct" : value < 75 ? "articulate" : "eloquent";
        }

        internal static uint HashPersonId(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    hash = (hash ^ (byte)character) * 16777619u;
                    hash = (hash ^ (byte)(character >> 8)) * 16777619u;
                }
                return hash;
            }
        }
    }
}
