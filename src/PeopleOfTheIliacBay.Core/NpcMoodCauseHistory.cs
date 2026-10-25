using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class NpcMoodCauseHistoryFloor
    {
        public string PersonId;
        public double SupportedFromAt;

        public static NpcMoodCauseHistoryFloor CopyValidated(NpcMoodCauseHistoryFloor source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.PersonId) ||
                source.PersonId.Length > 240 || double.IsNaN(source.SupportedFromAt) ||
                double.IsInfinity(source.SupportedFromAt) || source.SupportedFromAt < 0)
                throw new ArgumentException("Invalid NPC mood-cause history floor.");
            return new NpcMoodCauseHistoryFloor {
                PersonId = source.PersonId, SupportedFromAt = source.SupportedFromAt };
        }
    }

    public enum NpcMoodCauseHistoryAvailability { Available, UnavailableHistoricalDetail }

    public sealed class NpcMoodCauseHistoryRead
    {
        public NpcMoodCauseHistoryAvailability Availability { get; internal set; }
        public string Text { get; internal set; }
        public double SupportedFromAt { get; internal set; }
    }

    public sealed partial class WorldStateStore
    {
        private static List<NpcMoodCauseHistoryFloor> CopyNpcMoodCauseHistoryFloors(WorldState source,
            WorldState validated)
        {
            if (source.Version < 54)
            {
                Require(source.NpcMoodCauseHistoryFloors == null || source.NpcMoodCauseHistoryFloors.Count == 0,
                    "Legacy save contains future mood-cause history floors.");
                return new List<NpcMoodCauseHistoryFloor>();
            }
            Require(source.NpcMoodCauseHistoryFloors != null,
                "Missing mood-cause history floors.");
            var rows = new List<NpcMoodCauseHistoryFloor>(source.NpcMoodCauseHistoryFloors.Count);
            var owners = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in source.NpcMoodCauseHistoryFloors)
            {
                var row = NpcMoodCauseHistoryFloor.CopyValidated(raw);
                Require(owners.Add(row.PersonId) && KnownNpcIdentity(validated,row.PersonId,out _),
                    "Invalid mood-cause history floor owner.");
                rows.Add(row);
            }
            return rows;
        }

        public NpcMoodCauseHistoryRead ReadNpcMoodCauseHistory(string personId, double now,
            int maxChars = 600)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || maxChars < 1 || maxChars > 600)
                throw new ArgumentOutOfRangeException(nameof(now));
            var floor = state.NpcMoodCauseHistoryFloors.Find(x => x.PersonId == personId);
            if (floor != null && now < floor.SupportedFromAt)
                return new NpcMoodCauseHistoryRead {
                    Availability = NpcMoodCauseHistoryAvailability.UnavailableHistoricalDetail,
                    SupportedFromAt = floor.SupportedFromAt };
            return new NpcMoodCauseHistoryRead {
                Availability = NpcMoodCauseHistoryAvailability.Available,
                SupportedFromAt = floor == null ? 0 : floor.SupportedFromAt,
                Text = DescribeRecentConversationMoodCausesAvailable(personId, now, maxChars) };
        }
    }
}
