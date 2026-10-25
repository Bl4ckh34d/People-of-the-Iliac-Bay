using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Console/probe entry point. The only writes are diagnostic report files.
    public static class ActorCensusProbe
    {
        public static string LastPath { get; private set; }
        public static IEnumerator Run(WorldStateStore store)
        {
            var previous = ActorCensusSurvey.LastReport;
            ActorCensusReport report = null;
            yield return ActorCensusSurvey.Run(store, value => report = value);
            if (report == null) throw new InvalidOperationException("Actor census did not return a report.");
            var directory = Path.Combine(DaggerfallUnity.Settings.PersistentDataPath, "ActorCensus");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "actors-" + report.MapId + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".tsv");
            var records = report.Census.Snapshot();
            using (var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)))
            {
                writer.WriteLine("# read-only actor census v1; loaded scenes only; no adoption or gameplay writes");
                writer.WriteLine("# status=" + Cell(report.Status) + "; aborted=" + report.Aborted + "; nodes=" + report.VisitedNodes +
                    "; frames=" + report.Frames + "; maximumSliceMs=" + report.MaximumSliceMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                    "; readOnlyChecks=" + report.ReadOnlyChecks + "; observationCap=" + report.Census.Truncated +
                    "; nodeCap=" + report.TraversalTruncated + "; claimsCap=" + report.ClaimsTruncated);
                writer.WriteLine("# providerState=" + Cell(report.ProviderState));
                writer.WriteLine("# unknown claims do not mean no home; provider candidates do not prove actual renderer or layout ownership");
                writer.WriteLine("# identity collisions retain every live representation; session identities cannot be compared across reload");
                if (previous != null) writer.WriteLine("# previousSurveyComparison=" + Compare(previous, report));
                foreach (string provider in report.Providers) writer.WriteLine("# provider=" + Cell(provider));
                foreach (string error in report.Errors) writer.WriteLine("# error=" + Cell(error));
                writer.WriteLine("identity\tpersistent\tidentityCollision\tinstance\tactive\tsignature\tsource\tappearance\tappearanceProvider\trole\tbuilding\tquest\tservice\thome\tbed\tadoptionOrExclusion\tunknowns");
                int rows = 0; var watch = Stopwatch.StartNew();
                foreach (var record in records)
                    foreach (var observation in record.Observations)
                    {
                        writer.WriteLine(string.Join("\t", new[] { Cell(record.Identity), observation.PersistentIdentity.ToString(), record.IdentityCollision.ToString(),
                            Cell(observation.Instance), observation.Active.ToString(), Cell(observation.Signature), Cell(observation.Source), Cell(observation.Appearance),
                            Cell(observation.AppearanceProvider), Cell(observation.Role), Cell(observation.Building), Cell(observation.Quest), Cell(observation.Service),
                            Cell(observation.Home), Cell(observation.Bed), Cell(observation.Disposition), Cell(observation.Unknown) }));
                        if (++rows % 32 == 0 || watch.Elapsed.TotalMilliseconds >= 2) { yield return null; watch.Restart(); }
                    }
            }
            LastPath = path;
            UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTOR_CENSUS: " + path + "; identities=" + records.Count + "; observations=" + report.Census.ObservationCount +
                "; errors=" + report.Errors.Count + "; aborted=" + report.Aborted + "; native sampling/reload acceptance still requires the location matrix.");
        }
        public static string Compare(ActorCensusReport before, ActorCensusReport after)
        {
            var oldIds = StableIds(before); var newIds = StableIds(after);
            int shared = 0; foreach (var id in oldIds) if (newIds.Contains(id)) shared++;
            return "maps=" + before.MapId + "/" + after.MapId + "; sharedStable=" + shared + "; absentNow=" + (oldIds.Count - shared) +
                "; newNow=" + (newIds.Count - shared) + "; changes may reflect streaming or provider callbacks; inspect rows";
        }
        private static HashSet<string> StableIds(ActorCensusReport report)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in report.Census.Snapshot())
                if (!row.IdentityCollision && row.Observations.Count == 1 && row.Observations[0].PersistentIdentity) result.Add(row.Identity);
            return result;
        }
        private static string Cell(string value) { return (value ?? "unknown").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n"); }
    }
}
