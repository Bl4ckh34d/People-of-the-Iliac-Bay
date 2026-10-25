using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class PreparedOutdoorRoutine {public Plan Plan;public ResidentRoutine Routine,FallbackRoutine;}
        private sealed class OutdoorMigrationStats
        {
            public readonly System.Diagnostics.Stopwatch Wall=System.Diagnostics.Stopwatch.StartNew();
            public float NextReport;
            public long Plans,CurrentVersionSkips,GeometryRebuilds,GeometryRoutes,GeometryRouteSlices,GeometryRoutePoints;
            public long GeometryRouteCacheHits,GeometryRouteFailures,LaneRoutes,LaneRouteSlices,LaneRoutePoints;
            public long PersonalRebuilds,Published,Pending,MigrationFailures,Batches;
            public double BatchMilliseconds,BatchMaximumMilliseconds,GeometryRouteMilliseconds,GeometryRouteMaximumMilliseconds;
            public double LaneRouteMilliseconds,LaneRouteMaximumMilliseconds,PersonalRebuildMilliseconds,PersonalRebuildMaximumMilliseconds;
        }
        private OutdoorMigrationStats outdoorMigrationStats;
        public bool OutdoorRoutesPrepared {get;private set;}
        private IEnumerator OutdoorPreparationFrames(IEnumerator work,Func<bool> valid)
        {
            bool complete=false;
            Func<bool> canAdvance=()=>valid() && !GameManager.Instance.PlayerEnterExit.IsPlayerInside && !GameManager.IsGamePaused;
            try
            {
                while(!complete && valid())
                {
                    // The leaves retain their two-callback bound. Use spare frame
                    // budget across cheap/cache-hit leaves instead of one leaf/frame.
                    var budget=System.Diagnostics.Stopwatch.StartNew();
                    long started=RuntimeTiming.Start();
                    try {complete=BoundedWorkSlice.Advance(work,()=>budget.Elapsed.TotalMilliseconds,canAdvance);}
                    finally {RuntimeTiming.End("saved-outdoor-frame",started);}
                    var stats=outdoorMigrationStats;
                    if(stats!=null)
                    {
                        double elapsed=budget.Elapsed.TotalMilliseconds;stats.Batches++;stats.BatchMilliseconds+=elapsed;
                        stats.BatchMaximumMilliseconds=Math.Max(stats.BatchMaximumMilliseconds,elapsed);
                        RuntimeTiming.RecordDuration("saved-outdoor-batch",started);
                        if(Time.realtimeSinceStartup>=stats.NextReport || complete)
                        {LogOutdoorMigrationPerformance(complete ? "complete" : "progress");stats.NextReport=Time.realtimeSinceStartup+30;}
                    }
                    if(!complete) yield return null;
                }
            }
            finally
            {
                var disposable=work as IDisposable;if(disposable!=null)disposable.Dispose();
                if(outdoorMigrationStats!=null)
                {
                    if(!complete) LogOutdoorMigrationPerformance("cancelled");
                    if(geometry!=null) geometry.EndOutdoorMigrationMeasurements();
                    outdoorMigrationStats=null;
                }
            }
        }
        private IEnumerator PrepareRoutineLanes(ResidentRoutine routine,uint seed,Func<bool> valid)
        {
            var routes=new List<List<SavedRoutePoint>>();
            if(routine.Itinerary!=null)
            {foreach(var itinerary in ResidentRoutineDay.Itineraries(routine))
                {foreach(var stop in itinerary.Stops) routes.Add(stop.Route);routes.Add(itinerary.ExitRoute);}}
            else routes.Add(routine.Route);
            // The legacy route can differ from an itinerary only in old saves.
            if(routine.Itinerary!=null && routine.Route.Count>=2) routes.Add(routine.Route);
            foreach(var route in routes)
            {
                var stats=outdoorMigrationStats;
                if(stats!=null){stats.LaneRoutes++;stats.LaneRoutePoints+=route.Count;}
                var operation=geometry.PrepareStreetRouteSteps(route,seed,valid);
                while(true)
                {
                    while(GameManager.Instance.PlayerEnterExit.IsPlayerInside || GameManager.IsGamePaused) yield return null;
                    if(!valid()) break;
                    long started=stats!=null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                    bool more=operation.MoveNext();
                    if(stats!=null)
                    {
                        double elapsed=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000d/System.Diagnostics.Stopwatch.Frequency;
                        stats.LaneRouteSlices++;stats.LaneRouteMilliseconds+=elapsed;stats.LaneRouteMaximumMilliseconds=Math.Max(stats.LaneRouteMaximumMilliseconds,elapsed);
                    }
                    if(!more) break;
                    yield return null;
                }
                if(!valid()) yield break;
            }
        }
        private IEnumerator PrepareSavedOutdoorRoutines(SettlementPopulation expected,WorldStateStore store,
            TownRoutineState saved,int wake,int sleep)
        {
            OutdoorRoutesPrepared=false;
            outdoorMigrationStats=RuntimeTiming.Enabled ? new OutdoorMigrationStats() : null;
            if(outdoorMigrationStats!=null) geometry.BeginOutdoorMigrationMeasurements();
            Func<bool> valid=()=>directory && directory.gameObject.activeInHierarchy &&
                ReferenceEquals(population,expected) && ReferenceEquals(worldStore,store);
            dailyRouteCache.Clear();dailyRouteCacheOrder.Clear();dailyRouteCacheNodes.Clear();
            var pending=new List<PreparedOutdoorRoutine>();
            var prioritized=new List<Plan>(plans);
            var player=directory.transform.InverseTransformPoint(GameManager.Instance.PlayerObject.transform.position);
            // First usable routes belong to visible people and nearby households.
            // Never change the stable plan/identity order used by saved generation.
            prioritized.Sort((a,b)=>{
                bool visibleA=actors.ContainsKey(a.Resident.Id),visibleB=actors.ContainsKey(b.Resident.Id);
                if(visibleA!=visibleB)return visibleA?-1:1;
                double distanceA=PreparationDistance(a,player),distanceB=PreparationDistance(b,player);
                int order=distanceA.CompareTo(distanceB);return order!=0?order:string.CompareOrdinal(a.Resident.Id,b.Resident.Id);
            });
            foreach(var plan in prioritized)
            {
                if(!valid()) yield break;
                if(outdoorMigrationStats!=null) outdoorMigrationStats.Plans++;
                var source=plan.Routine;ResidentRoutine candidate=source;
                // Saved towns may omit residents whose runtime fallback is HomeDay.
                // No executable outdoor leg exists to migrate or publish for them.
                // SetResidentRoutine deliberately requires a persisted source entry.
                if(source.Itinerary==null && source.Route.Count<2)
                {
                    var repair=new PreparedOutdoorRoutine {Plan=plan};
                    var repairWork=RepairMissingAdultOuting(plan,repair,valid,wake,sleep);
                    while(repairWork.MoveNext()) {yield return null;if(!valid()) yield break;}
                    if(repair.Routine==null)
                    {
                        // An Inn-only resident has no daytime repair. Let the
                        // existing evening slots build its first real outing.
                        if(!saved.Residents.Exists(r=>r.ResidentId==plan.Resident.Id))continue;
                        var evening=new PreparedOutdoorRoutine {Plan=plan};
                        var eveningWork=ExpandAdultDailyOutings(plan,source,evening,valid,wake,sleep);
                        try{while(eveningWork.MoveNext()){yield return null;if(!valid())yield break;}}
                        finally{(eveningWork as IDisposable)?.Dispose();}
                        if(evening.Routine!=null && !TryPublishOutdoorRoutine(evening,store,wake,sleep))pending.Add(evening);
                        continue;
                    }
                    candidate=repair.Routine;
                }
                // A personal path is the saved executable route, including its
                // timing. Scene cache absence must never change that schedule.
                if(ReferenceEquals(candidate,source) && source.PersonalLaneVersion==OutdoorRoutineMigration.CurrentPersonalLaneVersion)
                {
                    if(outdoorMigrationStats!=null) outdoorMigrationStats.CurrentVersionSkips++;
                    candidate=RefreshDailyServiceHours(source);
                    if(candidate==null){preparedHouseholdRoutineSources.Add(source);continue;}
                    candidate=DailyOutingDistribution.Spread(candidate,plan.Resident.AppearanceSeed,wake,sleep,DailyReturnDeadline(sleep));
                    var baseline=candidate;
                    var expansion=new PreparedOutdoorRoutine {Plan=plan};
                    var expansionWork=ExpandAdultDailyOutings(plan,candidate,expansion,valid,wake,sleep);
                    try{while(expansionWork.MoveNext()){yield return null;if(!valid())yield break;}}
                    finally{(expansionWork as IDisposable)?.Dispose();}
                    if(expansion.Routine!=null)candidate=expansion.Routine;
                    if(!ReferenceEquals(candidate,source))
                    {
                        var timing=new PreparedOutdoorRoutine {Plan=plan,Routine=candidate,
                            FallbackRoutine=expansion.Routine==null?null:baseline};
                        if(!TryPublishOutdoorRoutine(timing,store,wake,sleep)) pending.Add(timing);
                    }
                    else preparedHouseholdRoutineSources.Add(source);
                    continue;
                }
                if(ReferenceEquals(candidate,source) && OutdoorRoutineMigration.NeedsCenterlineRebuild(source))
                {
                    if(outdoorMigrationStats!=null) outdoorMigrationStats.GeometryRebuilds++;
                    var oldRoutes=new List<List<SavedRoutePoint>>();var keys=new List<DailyRouteKey>();
                    int from=plan.Resident.HomeKey;
                    if(source.Itinerary!=null)
                    {
                        foreach(var itinerary in ResidentRoutineDay.Itineraries(source))
                        {
                            from=plan.Resident.HomeKey;
                            foreach(var stop in itinerary.Stops)
                            {oldRoutes.Add(stop.Route);keys.Add(new DailyRouteKey {From=from,To=stop.BuildingKey});from=stop.BuildingKey;}
                            oldRoutes.Add(itinerary.ExitRoute);keys.Add(new DailyRouteKey {From=from,To=plan.Resident.HomeKey});
                        }
                    }
                    else if(source.Route.Count>=2)
                    {oldRoutes.Add(source.Route);keys.Add(new DailyRouteKey {From=from,To=source.DestinationKey});}
                    var replacements=new List<List<SavedRoutePoint>>();bool failed=false;
                    for(int i=0;i<oldRoutes.Count;i++)
                    {
                        var key=keys[i];var cached=GetDailyRoute(key.From,key.To);
                        var old=oldRoutes[i];
                        if(cached!=null && cached.Points.Count>=2 && SameRouteEndpoint(old[0],cached.Points[0]) &&
                            SameRouteEndpoint(old[old.Count-1],cached.Points[cached.Points.Count-1]))
                        {replacements.Add(cached.Points);if(outdoorMigrationStats!=null) outdoorMigrationStats.GeometryRouteCacheHits++;continue;}
                        var result=new TownGeometry.RouteResult();var operation=geometry.ReadSavedRouteSteps(old,result,valid);
                        if(outdoorMigrationStats!=null){outdoorMigrationStats.GeometryRoutes++;outdoorMigrationStats.GeometryRoutePoints+=old.Count;}
                        try {while(true)
                        {
                            while(GameManager.Instance.PlayerEnterExit.IsPlayerInside || GameManager.IsGamePaused) yield return null;
                            if(!valid()) yield break;
                            long started=RuntimeTiming.Start();bool more;
                            long measuredStarted=outdoorMigrationStats!=null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                            try {more=operation.MoveNext();}finally
                            {
                                RuntimeTiming.End("saved-outdoor-route-slice",started);
                                if(outdoorMigrationStats!=null)
                                {
                                    double elapsed=(System.Diagnostics.Stopwatch.GetTimestamp()-measuredStarted)*1000d/System.Diagnostics.Stopwatch.Frequency;
                                    outdoorMigrationStats.GeometryRouteSlices++;outdoorMigrationStats.GeometryRouteMilliseconds+=elapsed;
                                    outdoorMigrationStats.GeometryRouteMaximumMilliseconds=Math.Max(outdoorMigrationStats.GeometryRouteMaximumMilliseconds,elapsed);
                                }
                            }
                            if(!more) break;yield return null;
                        }}finally {var disposable=operation as IDisposable;if(disposable!=null)disposable.Dispose();}
                        if(result.Points==null || result.Points.Count<2) {failed=true;break;}
                        replacements.Add(result.Points);SurveyDailyRoute(key.From,key.To,result.Points);
                    }
                    if(failed)
                    {if(outdoorMigrationStats!=null) outdoorMigrationStats.GeometryRouteFailures++;Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_MIGRATION_DEFERRED: "+plan.Resident.Id+"; saved route cannot yet be reconstructed safely.");continue;}
                    try {candidate=OutdoorRoutineMigration.Rebuild(source,replacements,
                        ResidentMovement.WalkSpeedFor(plan.Resident.AppearanceSeed),DaggerfallUnity.Instance.WorldTime.TimeScale);}
                    catch(ArgumentException error)
                    {if(outdoorMigrationStats!=null) outdoorMigrationStats.GeometryRouteFailures++;Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_MIGRATION_DEFERRED: "+plan.Resident.Id+"; "+error.Message);continue;}
                }
                var lanes=PrepareRoutineLanes(candidate,plan.Resident.AppearanceSeed,valid);
                while(lanes.MoveNext()) {yield return null;if(!valid()) yield break;}
                var personalRoutes=new List<List<SavedRoutePoint>>();
                if(candidate.Itinerary!=null)
                {foreach(var itinerary in ResidentRoutineDay.Itineraries(candidate))
                    {foreach(var stop in itinerary.Stops) personalRoutes.Add(OffsetSavedRoute(stop.Route,plan.Resident.AppearanceSeed));
                    personalRoutes.Add(OffsetSavedRoute(itinerary.ExitRoute,plan.Resident.AppearanceSeed));}}
                else if(candidate.Route.Count>=2) personalRoutes.Add(OffsetSavedRoute(candidate.Route,plan.Resident.AppearanceSeed));
                long personalStarted=outdoorMigrationStats!=null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                try {candidate=OutdoorRoutineMigration.Rebuild(candidate,personalRoutes,
                    ResidentMovement.WalkSpeedFor(plan.Resident.AppearanceSeed),DaggerfallUnity.Instance.WorldTime.TimeScale,true);}
                catch(ArgumentException error)
                {if(outdoorMigrationStats!=null) outdoorMigrationStats.MigrationFailures++;Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_MIGRATION_DEFERRED: "+plan.Resident.Id+"; personal lane: "+error.Message);continue;}
                if(outdoorMigrationStats!=null)
                {
                    double elapsed=(System.Diagnostics.Stopwatch.GetTimestamp()-personalStarted)*1000d/System.Diagnostics.Stopwatch.Frequency;
                    outdoorMigrationStats.PersonalRebuilds++;outdoorMigrationStats.PersonalRebuildMilliseconds+=elapsed;
                    outdoorMigrationStats.PersonalRebuildMaximumMilliseconds=Math.Max(outdoorMigrationStats.PersonalRebuildMaximumMilliseconds,elapsed);
                }
                candidate=RefreshDailyServiceHours(candidate);
                if(candidate==null)continue;
                candidate=DailyOutingDistribution.Spread(candidate,plan.Resident.AppearanceSeed,wake,sleep,DailyReturnDeadline(sleep));
                var preparedBaseline=candidate;
                var expanded=new PreparedOutdoorRoutine {Plan=plan};
                var expandWork=ExpandAdultDailyOutings(plan,candidate,expanded,valid,wake,sleep);
                try{while(expandWork.MoveNext()){yield return null;if(!valid())yield break;}}
                finally{(expandWork as IDisposable)?.Dispose();}
                if(expanded.Routine!=null)candidate=expanded.Routine;
                var item=new PreparedOutdoorRoutine {Plan=plan,Routine=candidate,
                    FallbackRoutine=expanded.Routine==null?null:preparedBaseline};
                if(!TryPublishOutdoorRoutine(item,store,wake,sleep)) {pending.Add(item);if(outdoorMigrationStats!=null) outdoorMigrationStats.Pending++;}
                else if(outdoorMigrationStats!=null) outdoorMigrationStats.Published++;
                yield return null;
            }
            OutdoorRoutesPrepared=true;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_ROUTES_PREPARED: map="+expected.SettlementId+"; deferredActiveResidents="+pending.Count);
            // Keep current travel and service visits untouched. Publish only when
            // both effective schedules agree the same resident is at home.
            while(pending.Count>0 && valid())
            {
                for(int i=pending.Count-1;i>=0;i--)
                {
                    if(!valid()) yield break;
                    if(TryPublishOutdoorRoutine(pending[i],store,wake,sleep))
                    {
                        pending.RemoveAt(i);
                        if(outdoorMigrationStats!=null)
                        {outdoorMigrationStats.Published++;outdoorMigrationStats.Pending=Math.Max(0,outdoorMigrationStats.Pending-1);}
                    }
                    yield return null;
                }
            }
        }
        private void LogOutdoorMigrationPerformance(string phase)
        {
            var stats=outdoorMigrationStats;if(stats==null || geometry==null) return;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_MIGRATION_PERF: phase="+phase+"; elapsedS="+stats.Wall.Elapsed.TotalSeconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+
                "; plans="+stats.Plans+"; currentVersionSkips="+stats.CurrentVersionSkips+"; geometryRebuilds="+stats.GeometryRebuilds+
                "; geometryRoutes="+stats.GeometryRoutes+"; geometryRouteSlices="+stats.GeometryRouteSlices+"; geometryRoutePoints="+stats.GeometryRoutePoints+
                "; geometryRouteCacheHits="+stats.GeometryRouteCacheHits+"; geometryRouteFailures="+stats.GeometryRouteFailures+
                "; laneRoutes="+stats.LaneRoutes+"; laneRoutePoints="+stats.LaneRoutePoints+"; laneRouteSlices="+stats.LaneRouteSlices+
                "; laneCacheHits="+geometry.PreparedLaneCacheHits+"; laneCacheMisses="+geometry.PreparedLaneCacheMisses+
                "; laneSliceMs="+stats.LaneRouteMilliseconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+
                "; laneSliceMaxMs="+stats.LaneRouteMaximumMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+
                "; crossSectionHits="+geometry.StreetCrossSectionCacheHits+"; crossSectionMisses="+geometry.StreetCrossSectionCacheMisses+
                "; crossSectionMs="+geometry.StreetCrossSectionSurveyMilliseconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+
                "; crossSectionMaxMs="+geometry.StreetCrossSectionSurveyMaximumMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+
                "; corridorStepCalls="+geometry.CorridorStepCalls+"; corridorStepMs="+geometry.CorridorStepMilliseconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+
                "; corridorStepMaxMs="+geometry.CorridorStepMaximumMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+
                "; clearanceChecks="+geometry.CorridorClearanceChecks+"; clearanceMs="+geometry.CorridorClearanceMilliseconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+
                "; clearanceMaxMs="+geometry.CorridorClearanceMaximumMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+
                "; batches="+stats.Batches+"; batchMs="+stats.BatchMilliseconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+
                "; batchMaxMs="+stats.BatchMaximumMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+
                "; routeRebuildMs="+stats.GeometryRouteMilliseconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+
                "; routeRebuildMaxMs="+stats.GeometryRouteMaximumMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+
                "; personalRebuilds="+stats.PersonalRebuilds+"; personalRebuildMs="+stats.PersonalRebuildMilliseconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+
                "; personalRebuildMaxMs="+stats.PersonalRebuildMaximumMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+
                "; published="+stats.Published+"; pending="+stats.Pending+"; failures="+stats.MigrationFailures+
                "; sharedCells="+geometry.ClearanceCellCount+"; cellSurveys="+geometry.ClearanceCellSurveys+
                "; clearanceCacheHits="+geometry.ClearanceCacheHits+"; exactFallbacks="+geometry.ClearanceExactChecks+
                "; sharedRouteHits="+geometry.SharedRouteHits);
        }
        private static double PreparationDistance(Plan plan,Vector3 player)
        {
            if(plan.Routine.Route.Count==0)return double.MaxValue;
            var p=plan.Routine.Route[0];double x=p.X-player.x,z=p.Z-player.z;return x*x+z*z;
        }
        private bool TryPublishOutdoorRoutine(PreparedOutdoorRoutine prepared,WorldStateStore store,int wake,int sleep)
        {
            var plan=prepared.Plan;var candidate=prepared.Routine;
            if(!plans.Contains(plan)) return true;
            if(GameManager.IsGamePaused || IsAlarmed(plan.Resident.Id) || shopDepartures.ContainsKey(plan.Resident.Id)) return false;
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            double now=(double)clock.ToSeconds()+clock.Second-Math.Floor(clock.Second);
            if(RuntimeFamilyOwnsDay(plan,now))return false;
            if(store.IsResidentAway(plan.Resident.Id,now)) return false;
            var current=Scheduled(plan,now);
            if(current.BuildingKey!=plan.Resident.HomeKey || current.Activity!=Activity.Home && current.Activity!=Activity.Sleep) return false;
            if(!FamilyAllowsAddedOutings(plan,candidate))
            {
                if(prepared.FallbackRoutine==null)return false;
                candidate=prepared.FallbackRoutine;prepared.Routine=candidate;prepared.FallbackRoutine=null;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DAILY_EXPANSION_RECHECK: resident="+plan.Resident.Id+
                    "; childcareChanged=true; keptPreparedBaseline=true");
            }
            // Rebuild the effective day from exactly the graph being persisted.
            // Appointment starts/ends remain fixed. Both old and new effective
            // schedules must be at home before replacing their future travel.
            DailySchedule replacement;
            try
            {
                DailySchedule ordinary,haunted;
                BuildOutdoorSchedules(plan.Resident,candidate,wake,sleep,out ordinary,out haunted);
                bool restricted=population.SettlementId==1291010263 && store.RestrictHauntingRoutines(population.SettlementId,
                    GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),now);
                replacement=restricted ? haunted : ordinary;
                int wakeMinute=restricted ? Math.Max(wake*60,300) : wake*60;
                int sleepMinute=restricted ? Math.Min(sleep*60,1140) : sleep*60;
                var bookings=store.AppointmentsFor(plan.Resident.Id);
                var days=new HashSet<int>();
                int currentDay=(int)Math.Floor(now/86400);
                foreach(var booking in bookings)
                {
                    int day=(int)Math.Floor(booking.StartsAt/86400);
                    // Resolved appointments still reserve their physical day slot
                    // when AppointmentSchedule rebuilds after a scene/save load.
                    if(booking.Status=="cancelled" || booking.Place.SettlementId!=population.SettlementId || day<currentDay || !days.Add(day)) continue;
                    DailySchedule appointment;string reason;
                    if(!AppointmentDayPlanner.TryCreate(plan.Resident,candidate,day,bookings,wakeMinute,sleepMinute,out appointment,out reason)) return false;
                    if(day==currentDay) replacement=appointment;
                }
                if(!OutdoorRoutineMigration.CanPublishAtHome(store,plan.Resident.Id,now,current,
                    ObservedSchedule(plan,now,replacement),plan.Resident.HomeKey)) return false;
            }
            catch(ArgumentException) {return false;}
            if(!FamilyAllowsAddedOutings(plan,candidate))return false;
            if(!ReferenceEquals(candidate,plan.Routine)) store.SetResidentRoutine(population.SettlementId,candidate);
            appointmentSchedules.Remove(plan.Resident.Id);
            ReplacePlanForItinerary(plan.Resident,candidate,plan.SurnameSeed,wake,sleep);
            preparedHouseholdRoutineSources.Add(candidate);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_ROUTINE_READY: resident="+plan.Resident.Id+"; geometryVersion="+candidate.RouteGeometryVersion+
                "; personalLaneVersion="+candidate.PersonalLaneVersion+"; publication=home");
            return true;
        }
        private static bool SameRouteEndpoint(SavedRoutePoint a,SavedRoutePoint b)
        {return Math.Abs(a.X-b.X)<0.00001 && Math.Abs(a.Z-b.Z)<0.00001;}
    }
}
