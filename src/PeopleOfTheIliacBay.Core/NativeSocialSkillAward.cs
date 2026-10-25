using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum NativeSocialSkillKind { None = 0, Etiquette = 1, Streetwise = 2 }

    public static class NativeSocialSkillAward
    {
        public static NativeSocialSkillKind SkillFor(int nativeSocialGroup, string tone)
        {
            if (tone != "polite" && tone != "neutral" && tone != "apology")
                return NativeSocialSkillKind.None;
            // DFU's five native tone-modifier groups. Groups >= 5 use merchant
            // modifiers in TalkManager, but lack enough evidence for an XP category.
            if (nativeSocialGroup == 2 || nativeSocialGroup == 3)
                return tone == "polite" || tone == "apology"
                    ? NativeSocialSkillKind.Etiquette : NativeSocialSkillKind.None;
            if (nativeSocialGroup == 0 || nativeSocialGroup == 1 || nativeSocialGroup == 4)
                return NativeSocialSkillKind.Streetwise;
            return NativeSocialSkillKind.None;
        }
    }

    public sealed partial class WorldStateStore
    {
        /// <summary>
        /// Reserve one native skill-use tally per known NPC per game day, only after the exact
        /// accepted social exchange and positive observed affinity receipt are persisted.
        /// Caller must tally the returned native skill after this reservation succeeds.
        /// </summary>
        public bool TryMarkAcceptedSocialSkillUse(string personId, long committedSequence,
            string receiptId, int nativeSocialGroup, string tone, double at,
            out NativeSocialSkillKind skill)
        {
            skill = NativeSocialSkillKind.None;
            NativeSocialSkillKind candidate = NativeSocialSkillAward.SkillFor(nativeSocialGroup, tone);
            if (candidate == NativeSocialSkillKind.None || string.IsNullOrWhiteSpace(personId) ||
                string.IsNullOrWhiteSpace(receiptId) || committedSequence < 1 || !Finite(at) || at < 0)
                return false;
            var mood = state.ConversationMoods.Find(x => x.PersonId == personId);
            var attitude = state.ConversationAttitudes.Find(x => x.PersonId == personId);
            if (mood == null || mood.AcceptedTurnSequence != committedSequence || mood.UpdatedAt != at ||
                attitude == null || attitude.LastSocialSkillAwardSequence >= committedSequence)
                return false;
            double day = Math.Floor(at / 86400d);
            if (attitude.LastSocialSkillAwardSequence > 0 && attitude.LastSocialSkillAwardDay >= day)
                return false;
            var speech = state.NpcExperienceInbox.Entries.Find(x => x.ObserverId == personId &&
                x.ReceiptId == receiptId && x.ObservedTurnReceiptId == receiptId &&
                x.ObservedTurnSpeakerId == NpcDirectedRelationship.PlayerTargetId &&
                x.Origin == NpcExperienceEventOrigin.AttributedSpeech &&
                x.HasKnownImmediateAffinityDelta && x.ImmediateAffinityDelta > 0 &&
                x.AffinityExclusion == NpcObservedSpeechAffinityExclusion.None &&
                (x.AlreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Affinity) != 0 &&
                x.ObservedTurnGameTime <= at);
            if (speech == null) return false;
            var journal = state.ConversationMemories.Find(x => x.PersonId == personId);
            if (journal == null || journal.Entries.Count == 0 ||
                journal.Entries[journal.Entries.Count - 1].PlayerText != speech.ObservedTurnText)
                return false;
            attitude.LastSocialSkillAwardSequence = committedSequence;
            attitude.LastSocialSkillAwardDay = day;
            attitude.LastSocialSkillAwardReceiptId = receiptId;
            skill = candidate;
            return true;
        }
    }
}
