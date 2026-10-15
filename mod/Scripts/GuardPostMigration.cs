using System;
using System.Collections.Generic;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public sealed partial class GuardPopulation
    {
        private void MigrateDeferredPost(double now)
        {
            if(publicPostSites.Count==0 || publicPostSurveyPending) return;
            foreach(var guard in Roster.Guards)
            {
                if(!guard.IsAlive || guard.Duty!=GuardDuty.Posted || Roster.IsOnPatrol(guard.Id) ||
                    publicPostSites.Exists(p=>Math.Abs(p.X-guard.Post.X)<0.05 && Math.Abs(p.Z-guard.Post.Z)<0.05)) continue;
                var candidates=new List<SavedRoutePoint>(publicPostSites);
                candidates.Sort((a,b)=>(Math.Pow(a.X-guard.Post.X,2)+Math.Pow(a.Z-guard.Post.Z,2)).CompareTo(
                    Math.Pow(b.X-guard.Post.X,2)+Math.Pow(b.Z-guard.Post.Z,2)));
                foreach(var post in candidates)
                {
                    if(Roster.Guards.Exists(g=>g.Id!=guard.Id && g.IsAlive && Math.Pow(g.Post.X-post.X,2)+Math.Pow(g.Post.Z-post.Z,2)<0.64)) continue;
                    var route=geometry.RouteBetweenPoints(guard.Post,post);
                    if(route.Count<2) continue;
                    if(approachSurvey!=null) StopCoroutine(approachSurvey);
                    if(patrolSurvey!=null) StopCoroutine(patrolSurvey);
                    patrolTemplates.Clear();
                    foreach(var patrol in Roster.Patrols) patrolTemplates.Add(PatrolPairPlan.CopyValidated(patrol.Plan));
                    Roster.MovePost(guard.Id,route,now,1.3,12);
                    approachSurvey=StartCoroutine(SurveyApproaches(GameManagerNavigation()));
                    patrolSurvey=StartCoroutine(SurveyPatrols());
                    Debug.Log("LIVING_CITIES_GUARD_POST_MOVE: "+guard.Id);
                    return;
                }
            }
        }
        private DaggerfallWorkshop.Game.Utility.CityNavigation GameManagerNavigation()
        { return DaggerfallWorkshop.Game.GameManager.Instance.StreamingWorld.GetCurrentCityNavigation(); }
    }
}
