using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public static class ConversationEffects
    {
        public static int Reaction(int nativeReaction, int offset)
        { return Math.Max(-100, Math.Min(100, nativeReaction + Math.Min(5, offset))); }
        public static int AdjustReactionTier(int nativeTier, int offset)
        {
            int shift = offset <= -24 ? -2 : offset <= -8 ? -1 : offset >= 5 ? 1 : 0;
            return Math.Max(0, Math.Min(2, nativeTier + shift));
        }
    }

    [Serializable]
    public sealed class ConversationAttitude
    {
        public string PersonId;
        public int Offset;
        public int NativeBaseline;
        public bool HasNativeBaseline;
        public int NativeFactionId = -1;
        public bool HasNativeFactionId;
        public double Day;
        public int PositiveToday, NegativeToday;
        public int Insults;
        public int EncounterCount;
        public double LastEncounterAt;
        public List<string> Utterances = new List<string>();
    }

    public sealed partial class WorldStateStore
    {
        public int ConversationDisposition(string personId)
        {
            var item = state.ConversationAttitudes.Find(a => a.PersonId == personId);
            return item == null ? 0 : item.Offset;
        }

        public int ConversationEncounters(string personId)
        {
            var item = state.ConversationAttitudes.Find(a => a.PersonId == personId);
            return item == null ? 0 : item.EncounterCount;
        }

        public void RecordConversationEncounter(string personId, double now)
        {
            Require(!string.IsNullOrWhiteSpace(personId) && personId.Length <= 240 && Finite(now) && now >= 0,
                "Invalid conversation encounter.");
            var item = state.ConversationAttitudes.Find(a => a.PersonId == personId);
            if (item == null)
            {
                item = new ConversationAttitude { PersonId = personId, Day = Math.Floor(now / 86400) };
                state.ConversationAttitudes.Add(item);
            }
            if (item.EncounterCount < 1000000) item.EncounterCount++;
            item.LastEncounterAt = now;
        }

        public void SetConversationBaseline(string personId, int nativeReaction, double now, int nativeFactionId = -1)
        {
            Require(!string.IsNullOrWhiteSpace(personId) && personId.Length <= 240 &&
                nativeReaction >= -100 && nativeReaction <= 100 && nativeFactionId >= -1 &&
                Finite(now) && now >= 0,
                "Invalid conversation baseline.");
            var item = state.ConversationAttitudes.Find(a => a.PersonId == personId);
            if (item == null)
            {
                item = new ConversationAttitude { PersonId = personId, Day = Math.Floor(now / 86400) };
                state.ConversationAttitudes.Add(item);
            }
            item.NativeBaseline = nativeReaction;
            item.HasNativeBaseline = true;
            item.NativeFactionId = nativeFactionId;
            item.HasNativeFactionId = nativeFactionId >= 0;
        }

        public int SetConversationThreatDisposition(string personId, int nativeBaseline, double now)
        {
            Require(!string.IsNullOrWhiteSpace(personId) && personId.Length <= 240 &&
                nativeBaseline >= -100 && nativeBaseline <= 100 && Finite(now) && now >= 0,
                "Invalid family-threat disposition request.");
            var item = state.ConversationAttitudes.Find(a => a.PersonId == personId);
            if (item == null)
            {
                item = new ConversationAttitude { PersonId = personId, Day = Math.Floor(now / 86400) };
                state.ConversationAttitudes.Add(item);
            }
            item.NativeBaseline = nativeBaseline;
            item.HasNativeBaseline = true;
            item.Offset = Math.Max(-200, Math.Min(5, -100 - nativeBaseline));
            return item.Offset;
        }

        public bool TryGetConversationBaseline(string personId, out int nativeBaseline, out int offset,
            out int nativeFactionId)
        {
            nativeBaseline = offset = 0;
            nativeFactionId = -1;
            if (string.IsNullOrWhiteSpace(personId)) return false;
            var item = state.ConversationAttitudes.Find(a => a.PersonId == personId &&
                a.HasNativeBaseline && a.HasNativeFactionId);
            if (item == null) return false;
            nativeBaseline = item.NativeBaseline;
            offset = item.Offset;
            nativeFactionId = item.NativeFactionId;
            return true;
        }

        public bool TryGetActualConversationDisposition(string personId, out int disposition)
        {
            disposition = 0;
            if (string.IsNullOrWhiteSpace(personId)) return false;
            var item = state.ConversationAttitudes.Find(a => a.PersonId == personId && a.HasNativeBaseline);
            if (item == null) return false;
            disposition = ConversationEffects.Reaction(item.NativeBaseline, item.Offset);
            return true;
        }

        public int ApplyConversationTone(string personId, string tone, string utterance, double now)
        {
            Require(!string.IsNullOrWhiteSpace(personId) && personId.Length <= 240 && Finite(now) && now >= 0, "Invalid conversation identity/time.");
            int delta = tone == "insult" ? -10 : tone == "rude" ? -2 : tone == "polite" ? 1 : 0;
            if (delta == 0) return 0;
            var item = state.ConversationAttitudes.Find(a => a.PersonId == personId);
            if (item == null)
            {
                item = new ConversationAttitude { PersonId = personId, Day = Math.Floor(now / 86400) };
                state.ConversationAttitudes.Add(item);
            }
            double day = Math.Floor(now / 86400);
            if (day > item.Day) { item.Day = day; item.PositiveToday = item.NegativeToday = 0; item.Utterances.Clear(); }
            string line = LocalDialogue.NormalizeContext(utterance, 240).ToLowerInvariant();
            if (line.Length == 0) return 0;
            if (delta > 0)
            {
                if (item.Utterances.Contains(line) || item.Utterances.Count >= 32) return 0;
                item.Utterances.Add(line);
                delta = Math.Min(delta, 3 - item.PositiveToday);
            }
            if (tone == "insult")
            {
                delta = item.Insults > 0 ? -25 : -10;
                item.Insults = Math.Min(1000000, item.Insults + 1);
            }
            int before = item.Offset;
            // -200 reaches native -100 even from the highest native reaction.
            // It is the engine's absolute endpoint, not a gameplay penalty cap.
            item.Offset = Math.Max(-200, Math.Min(5, before + delta));
            delta = item.Offset - before;
            if (delta > 0) item.PositiveToday += delta; else item.NegativeToday = Math.Min(1000000, item.NegativeToday - delta);
            return delta;
        }

        private static List<ConversationAttitude> CopyConversationAttitudes(WorldState source)
        {
            var result = new List<ConversationAttitude>();
            // Optional additive field: old schema-20 saves have no conversation records.
            if (source.ConversationAttitudes == null) return result;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in source.ConversationAttitudes)
            {
                Require(item != null && !string.IsNullOrWhiteSpace(item.PersonId) && item.PersonId.Length <= 240 && ids.Add(item.PersonId) &&
                    item.Offset >= -200 && item.Offset <= 20 && Finite(item.Day) && item.Day >= 0 && item.Day == Math.Floor(item.Day) &&
                    (!item.HasNativeBaseline || (item.NativeBaseline >= -100 && item.NativeBaseline <= 100)) &&
                    (!item.HasNativeFactionId || item.NativeFactionId >= 0) &&
                    item.PositiveToday >= 0 && item.PositiveToday <= 3 && item.NegativeToday >= 0 && item.NegativeToday <= 1000000 &&
                    item.Insults >= 0 && item.Insults <= 1000000 &&
                    item.EncounterCount >= 0 && item.EncounterCount <= 1000000 &&
                    Finite(item.LastEncounterAt) && item.LastEncounterAt >= 0 &&
                    item.Utterances != null && item.Utterances.Count <= 32, "Invalid conversation attitude.");
                var copy = new ConversationAttitude { PersonId = item.PersonId, Offset = Math.Min(5, item.Offset),
                    NativeBaseline = item.NativeBaseline, HasNativeBaseline = item.HasNativeBaseline,
                    NativeFactionId = item.HasNativeFactionId ? item.NativeFactionId : -1,
                    HasNativeFactionId = item.HasNativeFactionId, Day = item.Day,
                    PositiveToday = item.PositiveToday, NegativeToday = item.NegativeToday, Insults = item.Insults,
                    EncounterCount = item.EncounterCount, LastEncounterAt = item.LastEncounterAt };
                foreach (string line in item.Utterances)
                {
                    Require(line != null && line.Length <= 240, "Invalid conversation utterance.");
                    copy.Utterances.Add(line);
                }
                result.Add(copy);
            }
            return result;
        }
    }
}
