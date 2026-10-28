using System;
using System.Collections.Generic;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public enum ConversationSpeechClassificationSource
    {
        AcceptedSemanticDecisionRoute = 1
    }

    public enum ConversationSpeechClassificationKind
    {
        FamilyThreat = 1
    }

    /// <summary>A typed route classification tied to the exact observed player utterance.</summary>
    [Serializable]
    public sealed class ConversationSpeechClassification
    {
        public ConversationSpeechClassificationKind Kind;
        public ConversationSpeechClassificationSource Source;
        public string ObserverId, SpeakerId, ReceiptId, Utterance;
        public long Occurrence;
        public double ObservedGameTime;

        internal static ConversationSpeechClassification CopyValidated(ConversationSpeechClassification source,
            string expectedObserverId, string scopeId = null)
        {
            if (source == null) return null;
            if (source.Kind != ConversationSpeechClassificationKind.FamilyThreat ||
                source.Source != ConversationSpeechClassificationSource.AcceptedSemanticDecisionRoute ||
                string.IsNullOrWhiteSpace(expectedObserverId) || source.ObserverId != expectedObserverId ||
                source.SpeakerId != NpcDirectedRelationship.PlayerTargetId || source.Occurrence < 1 ||
                source.ReceiptId != ReceiptIdFor(source.Occurrence) &&
                    (string.IsNullOrWhiteSpace(scopeId) || source.ReceiptId !=
                        NpcObservedSpeechReceipt.IssuedReceiptId(scopeId, expectedObserverId, source.Occurrence)) ||
                string.IsNullOrWhiteSpace(source.Utterance) ||
                source.Utterance.Length > 1024 || double.IsNaN(source.ObservedGameTime) ||
                double.IsInfinity(source.ObservedGameTime) || source.ObservedGameTime < 0)
                throw new ArgumentException("Invalid receipt-bound conversation speech classification.", nameof(source));
            return new ConversationSpeechClassification { Kind = source.Kind, Source = source.Source,
                ObserverId = source.ObserverId, SpeakerId = source.SpeakerId, ReceiptId = source.ReceiptId,
                Utterance = source.Utterance, Occurrence = source.Occurrence, ObservedGameTime = source.ObservedGameTime };
        }

        private static string ReceiptIdFor(long occurrence)
        { return "native-heard-receipt-" + occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    }

    public static class ConversationEffects
    {
        public static double FriendshipIntervalSeconds(string personId, int progressionStage)
        {
            if (string.IsNullOrWhiteSpace(personId) || personId.Length > 240 ||
                progressionStage < 0 || progressionStage > 1000000)
                throw new ArgumentException("Invalid friendship accrual identity or progression stage.");

            uint hash = 2166136261u;
            byte[] bytes = Encoding.UTF8.GetBytes("friendship-accrual-v1:" + personId);
            unchecked
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= 16777619u;
                }
            }
            double factor = (8500 + hash % 3001) / 10000d;
            double nominalStageInterval = Math.Min(129600d, 86400d + 21600d * progressionStage);
            return Math.Min(129600d, nominalStageInterval * factor);
        }

        public static int Reaction(int nativeReaction, int offset)
        { return Math.Max(-100, Math.Min(100, nativeReaction + offset)); }
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
        public double PositiveCredit = 5;
        public double PositiveCreditUpdatedAt;
        public int PositiveProgressionStage;
        public double PositiveStageProgress;
        public int PositiveEarned;
        public int PositiveThisEncounter;
        public int Insults;
        public int EncounterCount;
        public double LastEncounterAt;
        public int SharedExchangeCount, FriendlyInitiatedCount;
        public double LastFriendlyInitiatedAt;
        public long LastSocialSkillAwardSequence;
        public double LastSocialSkillAwardDay;
        public string LastSocialSkillAwardReceiptId;
        public ConversationSpeechClassification FamilyThreatClassification;
        public NpcConversationBoundaryRecord Boundary;
        public List<string> Utterances = new List<string>();
    }

    public sealed partial class WorldStateStore
    {
        /// <summary>
        /// Records a semantic-route classification only after the runtime has validated route stage, submitted
        /// text, session, and observer bindings. Classification does not apply disposition or other effects.
        /// </summary>
        public bool TryRecordAcceptedFamilyThreatClassification(string personId, string expectedUtterance,
            NpcObservedSpeechReceipt speech, ConversationSpeechClassificationSource source)
        {
            return false;
        }

        public bool TryRecordAcceptedFamilyThreatClassification(string personId, string expectedUtterance,
            NpcObservedSpeechTicket ticket, NpcObservedSpeechReceipt speech,
            ConversationSpeechClassificationSource source)
        {
            if (string.IsNullOrWhiteSpace(personId) || personId.Length > 240 || speech == null ||
                string.IsNullOrWhiteSpace(expectedUtterance) || expectedUtterance.Length > 1024 ||
                source != ConversationSpeechClassificationSource.AcceptedSemanticDecisionRoute ||
                !CurrentObservedSpeechTicket(ticket) || ticket.PersonId != personId ||
                ticket.ExactText != expectedUtterance) return false;
            ConversationSpeechClassification candidate;
            try
            {
                var validated = NpcObservedSpeechReceipt.CopyValidated(speech);
                var evidence = validated.Evidence;
                var turn = validated.Turn;
                if (evidence.Origin != NpcExperienceEventOrigin.AttributedSpeech || evidence.ClaimedEventKind.HasValue ||
                    evidence.ObserverId != personId || evidence.SubjectPersonId != personId ||
                    evidence.AttributedSpeakerId != NpcDirectedRelationship.PlayerTargetId ||
                    turn.ObserverId != personId || turn.SpeakerId != NpcDirectedRelationship.PlayerTargetId ||
                    turn.ReceiptId != evidence.ReceiptId || turn.ObservedGameTime != evidence.ObservedGameTime ||
                    !string.Equals(turn.Text, expectedUtterance, StringComparison.Ordinal))
                    return false;
                string receiptId = evidence.ReceiptId;
                if (receiptId != ticket.IssuedReceipt.Evidence.ReceiptId ||
                    evidence.EpisodeId != ticket.IssuedReceipt.Evidence.EpisodeId ||
                    evidence.UtteranceId != ticket.IssuedReceipt.Evidence.UtteranceId ||
                    turn.ObservedGameTime != ticket.ObservedGameTime)
                    return false;
                candidate = new ConversationSpeechClassification { Kind = ConversationSpeechClassificationKind.FamilyThreat,
                    Source = source, ObserverId = personId, SpeakerId = turn.SpeakerId, ReceiptId = receiptId,
                    Utterance = turn.Text, Occurrence = ticket.Ordinal, ObservedGameTime = turn.ObservedGameTime };
                candidate = ConversationSpeechClassification.CopyValidated(candidate, personId, NpcFrameworkScopeId);
            }
            catch (ArgumentException) { return false; }

            var attitude = state.ConversationAttitudes.Find(item => item.PersonId == personId);
            var current = attitude == null ? null : attitude.FamilyThreatClassification;
            if (current != null)
            {
                if (candidate.ReceiptId == current.ReceiptId)
                    return candidate.Kind == current.Kind && candidate.Source == current.Source &&
                        candidate.ObserverId == current.ObserverId && candidate.SpeakerId == current.SpeakerId &&
                        candidate.Utterance == current.Utterance && candidate.Occurrence == current.Occurrence &&
                        candidate.ObservedGameTime == current.ObservedGameTime;
                // Legacy occurrence used a global inbox revision. New issued ordinals are
                // per person, so their numeric values are comparable only within v2.
                bool sameIssuedDomain = current.ReceiptId != null &&
                    current.ReceiptId.StartsWith("native-heard-v2-receipt-", StringComparison.Ordinal);
                if (sameIssuedDomain && candidate.Occurrence <= current.Occurrence ||
                    candidate.ObservedGameTime < current.ObservedGameTime)
                    return false;
            }
            if (attitude == null)
            {
                attitude = new ConversationAttitude { PersonId = personId,
                    Day = Math.Floor(candidate.ObservedGameTime / 86400d) };
                state.ConversationAttitudes.Add(attitude);
            }
            attitude.FamilyThreatClassification = candidate;
            return true;
        }

        /// <summary>Returns a detached, time-bounded copy of the latest accepted semantic classification.</summary>
        public ConversationSpeechClassification LatestAcceptedConversationFamilyThreat(string personId, double now)
        {
            Require(Finite(now) && now >= 0, "Invalid conversation time.");
            if (string.IsNullOrWhiteSpace(personId)) return null;
            var attitude = state.ConversationAttitudes.Find(item => item.PersonId == personId);
            if (attitude == null || attitude.FamilyThreatClassification == null ||
                attitude.FamilyThreatClassification.ObservedGameTime > now) return null;
            return ConversationSpeechClassification.CopyValidated(attitude.FamilyThreatClassification, personId,
                NpcFrameworkScopeId);
        }

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
            item.PositiveThisEncounter = 0;
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

        /// <summary>Apply a receipt-bound boundary's minimum hostility without softening prior hostility.</summary>
        public int SetConversationBoundaryDispositionAtMost(string personId,int nativeBaseline,int ceiling,double now)
        {
            Require(!string.IsNullOrWhiteSpace(personId) && personId.Length<=240 &&
                nativeBaseline>=-100 && nativeBaseline<=100 &&
                (ceiling==-50 || ceiling==-100) && Finite(now) && now>=0,
                "Invalid conversation boundary disposition request.");
            var item=state.ConversationAttitudes.Find(a=>a.PersonId==personId);
            if(item==null)
            {
                item=new ConversationAttitude {PersonId=personId,Day=Math.Floor(now/86400d)};
                state.ConversationAttitudes.Add(item);
            }
            int previousOffset=item.Offset;
            int previousEffective=ConversationEffects.Reaction(
                item.HasNativeBaseline?item.NativeBaseline:nativeBaseline,item.Offset);
            item.NativeBaseline=nativeBaseline;
            item.HasNativeBaseline=true;
            item.Offset=Math.Max(-200,Math.Min(item.Offset,
                Math.Min(ceiling-nativeBaseline,previousEffective-nativeBaseline)));
            return item.Offset-previousOffset;
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
                UpdatePositiveCredit(item, now);
                delta = Math.Min(delta, Math.Min(5 - item.PositiveThisEncounter,
                    Math.Min((int)Math.Floor(item.PositiveCredit), 100 - item.Offset)));
                if (delta <= 0) return 0;
            }
            if (tone == "insult")
            {
                delta = item.Insults > 0 ? -25 : -10;
                item.Insults = Math.Min(1000000, item.Insults + 1);
            }
            int before = item.Offset;
            // -200 reaches native -100 even from the highest native reaction.
            // It is the engine's absolute endpoint, not a gameplay penalty cap.
            item.Offset = Math.Max(-200, Math.Min(100, before + delta));
            delta = item.Offset - before;
            if (delta > 0)
            {
                item.PositiveToday = Math.Min(1000000, item.PositiveToday + delta);
                item.PositiveThisEncounter = Math.Min(5, item.PositiveThisEncounter + delta);
                item.PositiveCredit = Math.Max(0, item.PositiveCredit - delta);
                item.PositiveEarned = Math.Min(1000000, item.PositiveEarned + delta);
                int beyondInitial = Math.Max(0, item.PositiveEarned - 5);
                item.PositiveProgressionStage = Math.Min(1000000, beyondInitial / 5);
                item.PositiveStageProgress = beyondInitial % 5;
            }
            else item.NegativeToday = Math.Min(1000000, item.NegativeToday - delta);
            return delta;
        }

        internal static void UpdatePositiveCredit(ConversationAttitude item, double now)
        {
            if (now <= item.PositiveCreditUpdatedAt) return;
            double elapsed = now - item.PositiveCreditUpdatedAt;
            double interval = ConversationEffects.FriendshipIntervalSeconds(item.PersonId, item.PositiveProgressionStage);
            item.PositiveCredit = Math.Min(5, item.PositiveCredit + elapsed * 5 / interval);
            item.PositiveCreditUpdatedAt = now;
        }

        private static List<ConversationAttitude> CopyConversationAttitudes(WorldState source)
        {
            var result = new List<ConversationAttitude>();
            Require(source.ConversationAttitudes != null,"Missing conversation attitudes.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in source.ConversationAttitudes)
            {
                int offset = item == null ? 0 : item.Offset;
                double positiveCredit = item == null ? 0 : item.PositiveCredit;
                double positiveCreditUpdatedAt = item == null ? 0 : item.PositiveCreditUpdatedAt;
                int positiveEarned = item == null ? 0 : item.PositiveEarned;
                int progressionStage = item == null ? 0 : item.PositiveProgressionStage;
                double stageProgress = item == null ? 0 : item.PositiveStageProgress;
                int positiveThisEncounter = item == null ? 0 : item.PositiveThisEncounter;
                Require(item != null && !string.IsNullOrWhiteSpace(item.PersonId) && item.PersonId.Length <= 240 && ids.Add(item.PersonId) &&
                    offset >= -200 && offset <= 100 && Finite(item.Day) && item.Day >= 0 && item.Day == Math.Floor(item.Day) &&
                    (!item.HasNativeBaseline || (item.NativeBaseline >= -100 && item.NativeBaseline <= 100)) &&
                    (!item.HasNativeFactionId || item.NativeFactionId >= 0) &&
                    item.PositiveToday >= 0 && item.PositiveToday <= 1000000 && item.NegativeToday >= 0 && item.NegativeToday <= 1000000 &&
                    Finite(positiveCredit) && positiveCredit >= 0 && positiveCredit <= 5 &&
                    Finite(positiveCreditUpdatedAt) && positiveCreditUpdatedAt >= 0 &&
                    progressionStage >= 0 && progressionStage <= 1000000 &&
                    Finite(stageProgress) && stageProgress >= 0 && stageProgress < 5 &&
                    positiveEarned >= 0 && positiveEarned <= 1000000 &&
                    progressionStage == Math.Max(0, positiveEarned - 5) / 5 &&
                    stageProgress == Math.Max(0, positiveEarned - 5) % 5 &&
                    positiveThisEncounter >= 0 && positiveThisEncounter <= 5 &&
                    item.Insults >= 0 && item.Insults <= 1000000 &&
                    item.EncounterCount >= 0 && item.EncounterCount <= 1000000 &&
                    Finite(item.LastEncounterAt) && item.LastEncounterAt >= 0 &&
                    item.SharedExchangeCount>=0 && item.SharedExchangeCount<=1000000 &&
                    item.LastSocialSkillAwardSequence >= 0 &&
                    Finite(item.LastSocialSkillAwardDay) && item.LastSocialSkillAwardDay >= 0 &&
                    (item.LastSocialSkillAwardSequence == 0 ||
                        !string.IsNullOrWhiteSpace(item.LastSocialSkillAwardReceiptId)) &&
                    (item.LastSocialSkillAwardReceiptId == null || item.LastSocialSkillAwardReceiptId.Length <= 240) &&
                    item.FriendlyInitiatedCount>=0 && item.FriendlyInitiatedCount<=1000000 &&
                    Finite(item.LastFriendlyInitiatedAt) && item.LastFriendlyInitiatedAt>=0 &&
                    (item.FriendlyInitiatedCount>0 || item.LastFriendlyInitiatedAt==0) &&
                    (item.FamilyThreatClassification == null ||
                        IsValidFamilyThreatClassification(item.FamilyThreatClassification, item.PersonId,
                            source.NpcFrameworkScopeId)) &&
                    item.Utterances != null && item.Utterances.Count <= 32, "Invalid conversation attitude.");
                var copy = new ConversationAttitude { PersonId = item.PersonId, Offset = offset,
                    NativeBaseline = item.NativeBaseline, HasNativeBaseline = item.HasNativeBaseline,
                    NativeFactionId = item.HasNativeFactionId ? item.NativeFactionId : -1,
                    HasNativeFactionId = item.HasNativeFactionId, Day = item.Day,
                    PositiveToday = item.PositiveToday, NegativeToday = item.NegativeToday, Insults = item.Insults,
                    PositiveCredit = positiveCredit, PositiveCreditUpdatedAt = positiveCreditUpdatedAt,
                    PositiveProgressionStage = progressionStage,
                    PositiveStageProgress = stageProgress, PositiveEarned = positiveEarned,
                    PositiveThisEncounter = positiveThisEncounter,
                    EncounterCount = item.EncounterCount, LastEncounterAt = item.LastEncounterAt,
                    SharedExchangeCount=item.SharedExchangeCount,FriendlyInitiatedCount=item.FriendlyInitiatedCount,
                    LastFriendlyInitiatedAt=item.LastFriendlyInitiatedAt,
                    LastSocialSkillAwardSequence=item.LastSocialSkillAwardSequence,
                    LastSocialSkillAwardDay=item.LastSocialSkillAwardDay,
                    LastSocialSkillAwardReceiptId=item.LastSocialSkillAwardReceiptId,
                    FamilyThreatClassification = ConversationSpeechClassification.CopyValidated(
                        item.FamilyThreatClassification, item.PersonId, source.NpcFrameworkScopeId),
                    Boundary = NpcConversationBoundaryRecord.CopyValidated(
                        item.Boundary, item.PersonId, source.NpcFrameworkScopeId) };
                foreach (string line in item.Utterances)
                {
                    Require(line != null && line.Length <= 240, "Invalid conversation utterance.");
                    copy.Utterances.Add(line);
                }
                result.Add(copy);
            }
            return result;
        }

        private static bool IsValidFamilyThreatClassification(ConversationSpeechClassification value,
            string personId, string scopeId)
        {
            try { ConversationSpeechClassification.CopyValidated(value, personId, scopeId); return true; }
            catch (ArgumentException) { return false; }
        }
    }
}
