using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private IEnumerator postMigrationWork;
        private void CancelPostMigration()
        {
            (postMigrationWork as IDisposable)?.Dispose();
            postMigrationWork=null;
        }
        private void UpdatePostMigration()
        {
            if(publicPostSites.Count==0 || publicPostSurveyPending || !OwnsExterior) return;
            if(postMigrationWork==null)
            {
                if(Time.unscaledTime<nextPostMigration || !BackgroundWork.TryStart()) return;
                postMigrationWork=MigrateDeferredPosts();
            }
            long started=RuntimeTiming.Start();
            try
            {
                if(!postMigrationWork.MoveNext())
                { CancelPostMigration(); nextPostMigration=Time.unscaledTime+5; }
            }
            catch { CancelPostMigration(); throw; }
            finally { RuntimeTiming.End("guard-post-migration-slice",started); }
        }
        private bool CanMigratePost(GuardRecord guard)
        {
            return guard.IsAlive && guard.Duty==GuardDuty.Posted && guard.Interior==null && guard.Refuge==null &&
                !Roster.IsOnPatrol(guard.Id) && !IsGateGuard(guard.Id) &&
                !publicPostSites.Exists(p=>Math.Abs(p.X-guard.Post.X)<0.05 && Math.Abs(p.Z-guard.Post.Z)<0.05);
        }
        private bool PostAvailable(GuardRecord guard,SavedRoutePoint post)
        { return !Roster.Guards.Exists(g=>g.Id!=guard.Id && g.IsAlive && Gap(g.Post,post)<0.8); }

        private IEnumerator MigrateDeferredPosts()
        {
            var roster=Roster; var townGeometry=geometry;
            foreach(var guard in new List<GuardRecord>(roster.Guards))
            {
                yield return null;
                if(!CanMigratePost(guard)) continue;
                var source=new SavedRoutePoint {X=guard.Post.X,Z=guard.Post.Z};
                Func<bool> valid=()=>Roster==roster && geometry==townGeometry && OwnsExterior &&
                    CanMigratePost(guard) && Gap(source,guard.Post)<0.001;
                // An obstructed old post cannot start any relocation route. Check
                // once instead of repeating the same failure for every destination.
                var startResult=new TownGeometry.RouteResult();
                var startWork=townGeometry.ReadRouteSteps(source,source,startResult,valid);
                try {while(startWork.MoveNext()) yield return null;}
                finally {(startWork as IDisposable)?.Dispose();}
                if(!valid() || startResult.Points==null || startResult.Points.Count<2) continue;
                var candidates=new List<SavedRoutePoint>(publicPostSites);
                candidates.Sort((a,b)=>Gap(a,source).CompareTo(Gap(b,source)));
                foreach(var post in candidates)
                {
                    yield return null;
                    if(!valid()) break;
                    if(!PostAvailable(guard,post)) continue;
                    var result=new TownGeometry.RouteResult();
                    var routeWork=townGeometry.ReadRouteSteps(source,post,result,valid);
                    try {while(routeWork.MoveNext()) yield return null;}
                    finally {(routeWork as IDisposable)?.Dispose();}
                    if(!valid()) break;
                    var route=result.Points;
                    if(route==null || route.Count<2 || !PostAvailable(guard,post)) continue;
                    if(approachSurvey!=null) StopCoroutine(approachSurvey);
                    if(patrolSurvey!=null) StopCoroutine(patrolSurvey);
                    patrolTemplates.Clear();
                    foreach(var patrol in Roster.Patrols) patrolTemplates.Add(PatrolPairPlan.CopyValidated(patrol.Plan));
                    Roster.MovePost(guard.Id,route,Now(),1.3,DaggerfallUnity.Instance.WorldTime.TimeScale);
                    approachSurvey=StartCoroutine(SurveyApproaches(GameManagerNavigation()));
                    patrolSurvey=StartCoroutine(SurveyPatrols());
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_POST_MOVE: "+guard.Id);
                    break;
                }
            }
        }
        private DaggerfallWorkshop.Game.Utility.CityNavigation GameManagerNavigation()
        { return DaggerfallWorkshop.Game.GameManager.Instance.StreamingWorld.GetCurrentCityNavigation(); }
    }
}
