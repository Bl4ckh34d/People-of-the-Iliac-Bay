using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        private const int MaximumConversationMoodCauseEntries = 3;
        private const int MaximumConversationMoodCauseCharacters = 600;
        private const string ConversationMoodCauseHeader =
            "Known past events still affecting your mood (other influences may exist): ";

        /// <summary>
        /// Describes a few recent, still-relevant mood effects recorded from consumed native outcomes in this
        /// store's current save branch. The remaining amounts are projections from accepted applied deltas,
        /// not an exact causal decomposition of current mood.
        /// </summary>
        public string DescribeRecentConversationMoodCauses(string personId, double now, int maxChars = 600)
        {
            var read = ReadNpcMoodCauseHistory(personId, now, maxChars);
            if (read.Availability != NpcMoodCauseHistoryAvailability.Available)
                throw new InvalidOperationException("Historical NPC mood-cause detail is unavailable.");
            return read.Text;
        }

        private string DescribeRecentConversationMoodCausesAvailable(string personId, double now, int maxChars)
        {
            if (!FiniteMoodCauseTime(now) || now < 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (maxChars < 1 || maxChars > MaximumConversationMoodCauseCharacters)
                throw new ArgumentOutOfRangeException(nameof(maxChars));

            bool observerAlive;
            if (!KnownNpcIdentity(state, personId, out observerAlive) || !observerAlive)
                return string.Empty;

            NpcExperienceInbox inbox = state.NpcExperienceInbox;
            if (inbox == null || inbox.Entries == null || inbox.Entries.Count == 0)
                return string.Empty;

            double halfLife = ConversationMoodHalfLifeSeconds(personId);
            if (!FiniteMoodCauseTime(halfLife) || halfLife <= 0)
                return string.Empty;

            var recent = new List<RecordedMoodCauseCandidate>(MaximumConversationMoodCauseEntries);
            foreach (NpcExperienceInboxEntry entry in inbox.Entries)
            {
                if (entry == null || entry.Origin != NpcExperienceEventOrigin.NativeOutcome ||
                    entry.Status != NpcExperienceInboxStatus.Consumed || entry.Revision != 2 ||
                    !string.Equals(entry.ObserverId, personId, StringComparison.Ordinal) || entry.Appraisal == null ||
                    entry.Appraisal.AppliedMoodDelta == 0 ||
                    entry.EventKind < NpcExperienceEventKind.MoneyReceived ||
                    entry.EventKind > NpcExperienceEventKind.ItemReceived ||
                    !FiniteMoodCauseTime(entry.ObservedGameTime) || !FiniteMoodCauseTime(entry.UpdatedAt) ||
                    entry.UpdatedAt < entry.ObservedGameTime || entry.ObservedGameTime > now || entry.UpdatedAt > now)
                    continue;

                int remainingEffect;
                try
                {
                    remainingEffect = MoodRecovery.ValueAt(entry.Appraisal.AppliedMoodDelta,
                        entry.UpdatedAt, now, halfLife);
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }
                if (remainingEffect == 0) continue;

                InsertRecentMoodCause(recent, new RecordedMoodCauseCandidate(entry, remainingEffect));
            }

            if (recent.Count == 0) return string.Empty;

            var output = new StringBuilder();
            foreach (RecordedMoodCauseCandidate candidate in recent)
            {
                string cause;
                try
                {
                    NpcExperienceEvent evidence = candidate.Entry.ToEvent();
                    cause = FormatRecordedMoodCause(evidence, personId, candidate.RemainingEffect);
                }
                catch (ArgumentException)
                {
                    // A corrupt or unsupported historical row cannot become conversational evidence.
                    continue;
                }

                string separator = output.Length == 0 ? ConversationMoodCauseHeader : " | ";
                if (output.Length + separator.Length + cause.Length > maxChars)
                {
                    if (output.Length == 0) return string.Empty;
                    break;
                }
                output.Append(separator);
                output.Append(cause);
            }
            return output.ToString();
        }

        private static void InsertRecentMoodCause(List<RecordedMoodCauseCandidate> recent,
            RecordedMoodCauseCandidate candidate)
        {
            int position = recent.Count;
            for (int i = 0; i < recent.Count; i++)
            {
                RecordedMoodCauseCandidate current = recent[i];
                if (candidate.Entry.UpdatedAt > current.Entry.UpdatedAt ||
                    candidate.Entry.UpdatedAt == current.Entry.UpdatedAt &&
                    StringComparer.Ordinal.Compare(candidate.Entry.DeduplicationKey,
                        current.Entry.DeduplicationKey) > 0)
                {
                    position = i;
                    break;
                }
            }

            if (position >= MaximumConversationMoodCauseEntries) return;
            recent.Insert(position, candidate);
            if (recent.Count > MaximumConversationMoodCauseEntries)
                recent.RemoveAt(recent.Count - 1);
        }

        private static string FormatRecordedMoodCause(NpcExperienceEvent evidence, string observerId,
            int remainingEffect)
        {
            var description = new StringBuilder(DescribeNativeMoodCause(evidence, observerId));
            description.Append(" (observed at game-second ");
            description.Append(evidence.ObservedGameTime.ToString("R", CultureInfo.InvariantCulture));
            description.Append(remainingEffect>0?"; still lifts your mood":"; still weighs on your mood");
            description.Append("; estimated remaining mood effect ");
            if (remainingEffect > 0) description.Append('+');
            description.Append(remainingEffect.ToString(CultureInfo.InvariantCulture));

            description.Append("; ");
            description.Append(DescribeResponsibleMoodCauseActor(evidence, observerId));

            description.Append(')');
            return description.ToString();
        }

        private static string DescribeResponsibleMoodCauseActor(NpcExperienceEvent evidence, string observerId)
        {
            switch (evidence.ResponsibleActorAttribution)
            {
                case NpcResponsibleActorAttribution.Unknown:
                    return "responsible actor unknown";
                case NpcResponsibleActorAttribution.NotApplicable:
                    return "responsibility not applicable";
                case NpcResponsibleActorAttribution.Known:
                    if (string.Equals(evidence.ResponsibleActorId, NpcDirectedRelationship.PlayerTargetId,
                        StringComparison.Ordinal))
                        return "responsible actor: player";
                    if (string.Equals(evidence.ResponsibleActorId, observerId, StringComparison.Ordinal))
                        return "responsible actor: self";
                    return "responsible actor: another person";
                default:
                    return "responsible actor unknown";
            }
        }

        private static string DescribeNativeMoodCause(NpcExperienceEvent evidence, string observerId)
        {
            NpcExperienceImpactDetails details = evidence.NativeImpactDetails;
            bool isAboutObserver = string.Equals(evidence.SubjectPersonId, observerId, StringComparison.Ordinal);
            string subject = isAboutObserver ? "you: " : "another person: ";
            switch (evidence.EventKind.Value)
            {
                case NpcExperienceEventKind.MoneyReceived:
                    return subject + "money received";
                case NpcExperienceEventKind.Injury:
                    if (isAboutObserver && details != null && details.Kind == NpcExperienceImpactKind.Health &&
                        details.TotalDamage > 0)
                        return subject + "injury (native damage " + details.TotalDamage.ToString(CultureInfo.InvariantCulture) + ")";
                    return subject + "injury";
                case NpcExperienceEventKind.Healing:
                    if (isAboutObserver && details != null && details.Kind == NpcExperienceImpactKind.Health &&
                        details.TotalHealing > 0)
                        return subject + "healing/recovery (native recovery " + details.TotalHealing.ToString(CultureInfo.InvariantCulture) + ")";
                    return subject + "healing/recovery";
                case NpcExperienceEventKind.StatusOnset:
                    if(details != null && details.Kind == NpcExperienceImpactKind.StatusOnset &&
                        details.Status == NpcExperienceStatusKind.Poison)
                        return isAboutObserver?"you were poisoned":"another person was poisoned";
                    return subject + "status onset";
                case NpcExperienceEventKind.AppointmentAttended:
                    return subject + "appointment attended";
                case NpcExperienceEventKind.AppointmentNoShow:
                    return subject + "appointment missed";
                case NpcExperienceEventKind.CombatHelp:
                    return subject + "combat help";
                case NpcExperienceEventKind.ObligationFulfilled:
                    return subject + "obligation fulfilled";
                case NpcExperienceEventKind.ObligationBreached:
                    return subject + "obligation breached";
                case NpcExperienceEventKind.GiftReceived:
                    return subject + "gift received";
                case NpcExperienceEventKind.ItemReceived:
                    return subject + "item received";
                default:
                    return subject + "native outcome";
            }
        }

        private static bool FiniteMoodCauseTime(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private sealed class RecordedMoodCauseCandidate
        {
            public readonly NpcExperienceInboxEntry Entry;
            public readonly int RemainingEffect;

            public RecordedMoodCauseCandidate(NpcExperienceInboxEntry entry, int remainingEffect)
            {
                Entry = entry;
                RemainingEffect = remainingEffect;
            }
        }
    }
}
