using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class CorpseSearchRecruitmentNativeProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        internal static IEnumerator Run(GuardPopulation guards,WorldStateStore store,BuildingDirectory directory,
            TownGeometry geometry,GuardReport report,GuardActor first)
        {
            Require(report.HasCorpseInquiryWindow && report.GuardIds.Count==1,"Recruitment fixture needs one cleared-body investigator.");
            var roster=guards.Roster;int census=roster.Guards.Count;double until=report.InvestigationUntil;
            typeof(GuardPopulation).GetMethod("CancelPostMigration",Private).Invoke(guards,null);
            ((List<SavedRoutePoint>)typeof(GuardPopulation).GetField("publicPostSites",Private).GetValue(guards)).Clear();
            foreach(string name in new[] {"approachSurvey","patrolSurvey"})
            {
                var field=typeof(GuardPopulation).GetField(name,Private);var work=field.GetValue(guards) as Coroutine;
                if(work!=null)guards.StopCoroutine(work);field.SetValue(guards,null);
            }
            var available=roster.Guards.FindAll(g=>g.IsAlive && g.Duty==GuardDuty.Posted && g.Interior==null &&
                g.Refuge==null && g.PostRecall==null && !roster.IsOnPatrol(g.Id));
            available.Sort((a,b)=>string.CompareOrdinal(a.Id,b.Id));
            Require(available.Count>=6,"Six existing free guards are required for relay/cap verification; available="+available.Count);

            List<SavedRoutePoint> posts=null;
            foreach(var destination in roster.Guards)
            {
                var path=geometry.RouteBetweenPoints(report.Entrance,destination.Post);if(path.Count<2)continue;
                var proposed=new List<SavedRoutePoint>();var previous=geometry.ScenePoint(new RoutePoint(report.Entrance.X,report.Entrance.Z));
                var origin=previous;
                foreach(var point in path)
                {
                    var scene=geometry.ScenePoint(new RoutePoint(point.X,point.Z));
                    float step=Vector3.Distance(previous,scene),outward=Vector3.Distance(origin,scene);
                    if(step<12 || step>30 || outward<(proposed.Count+1)*10 || outward>130 ||
                        roster.Guards.Exists(g=>g.IsAlive && Math.Abs(g.Post.X-point.X)+Math.Abs(g.Post.Z-point.Z)<0.8) ||
                        !CivilianResponses.Sees(previous+Vector3.up,scene+Vector3.up,35))continue;
                    proposed.Add(point);previous=scene;if(proposed.Count==6)break;
                }
                if(proposed.Count==6){posts=proposed;break;}
            }
            Require(posts!=null,"No surveyed chain of six reachable guard posts with visible contacts.");
            var allowed=new HashSet<string>{first.GuardId};var starts=new Dictionary<string,Vector3>();
            for(int i=0;i<6;i++)
            {
                var guard=available[i];roster.ReassignPost(guard.Id,posts[i],Now());allowed.Add(guard.Id);
                starts[guard.Id]=geometry.ScenePoint(new RoutePoint(posts[i].X,posts[i].Z))+Vector3.up;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SEARCH_CHAIN_STAGE: guard="+guard.Id+"; post="+starts[guard.Id]);
            }
            var views=(Dictionary<string,GuardActor>)typeof(GuardPopulation).GetField("actors",Private).GetValue(guards);
            var view=typeof(GuardPopulation).GetMethod("View",Private);
            foreach(var id in allowed)
            {
                var actor=(GuardActor)view.Invoke(guards,new object[]{roster.Guards.Find(g=>g.Id==id),Now()});
                actor.gameObject.SetActive(true);actor.Place(Now());
            }
            string extra=available[5].Id;bool relay=false;float capContactSeconds=0;var moved=new HashSet<string>();
            var previousMembers=new HashSet<string>(report.GuardIds);var oldDistances=new Dictionary<string,float>();
            float deadline=Time.realtimeSinceStartup+60;
            while(Time.realtimeSinceStartup<deadline)
            {
                // Keep unrelated test-town actors out of the contact scene. The
                // seven existing fixture guards still use production LateUpdate.
                foreach(var pair in views)if(pair.Value && !allowed.Contains(pair.Key))pair.Value.gameObject.SetActive(false);
                foreach(var id in report.GuardIds)
                {
                    GuardActor actor;Require(views.TryGetValue(id,out actor) && actor && actor.gameObject.activeInHierarchy,
                        "Search member has no physical loaded guard actor: "+id);
                    if(starts.ContainsKey(id) && Vector3.Distance(starts[id],actor.transform.position)>1)moved.Add(id);
                    float before;
                    if(!previousMembers.Contains(id) && oldDistances.TryGetValue(id,out before) && before>40)relay=true;
                    Require(!actor.CombatReady && roster.Guards.Find(g=>g.Id==id).LastSeenSuspect==null,
                        "Recruitment invented combat or suspect evidence.");
                }
                Require(report.GuardIds.Count<=6 && report.InvestigationUntil==until && roster.Guards.Count==census,
                    "Recruitment changed case deadline, census or six-member bound.");
                Require(GameManager.Instance.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None,
                    "Neutral corpse-search recruitment accused player.");
                if(report.GuardIds.Count==6)
                {
                    Require(!report.GuardIds.Contains(extra) && roster.Guards.Find(g=>g.Id==extra).Duty==GuardDuty.Posted,
                        "Seventh available guard joined a full search.");
                    GuardActor spare;bool contact=false;
                    if(views.TryGetValue(extra,out spare) && spare && spare.gameObject.activeInHierarchy)
                        foreach(var id in report.GuardIds)
                        {
                            var caller=views[id];
                            if(Vector3.Distance(caller.transform.position,spare.transform.position)<=35 &&
                                CivilianResponses.Sees(caller.transform.position,spare.transform.position,35))contact=true;
                        }
                    if(contact)capContactSeconds+=Time.unscaledDeltaTime;
                }
                previousMembers=new HashSet<string>(report.GuardIds);oldDistances.Clear();
                foreach(var id in allowed)
                {
                    GuardActor actor;if(views.TryGetValue(id,out actor) && actor)
                        oldDistances[id]=Vector3.Distance(first.transform.position,actor.transform.position);
                }
                if(report.GuardIds.Count==6 && moved.Count==5 && relay && capContactSeconds>=1 &&
                    report.GuardIds.TrueForAll(id=>roster.Guards.Find(g=>g.Id==id).Duty==GuardDuty.Searching))break;
                yield return null;
            }
            Require(report.GuardIds.Count==6 && moved.Count==5 && relay && capContactSeconds>=1 && !report.GuardIds.Contains(extra) &&
                report.GuardIds.TrueForAll(id=>roster.Guards.Find(g=>g.Id==id).Duty==GuardDuty.Searching),
                "Physical relay incomplete: members="+report.GuardIds.Count+"; moved="+moved.Count+"; relay="+relay+"; spareContactSeconds="+capContactSeconds);
            float capUntil=Time.realtimeSinceStartup+3;
            while(Time.realtimeSinceStartup<capUntil)
            {
                foreach(var pair in views)if(pair.Value && !allowed.Contains(pair.Key))pair.Value.gameObject.SetActive(false);
                Require(report.GuardIds.Count==6 && roster.Guards.Find(g=>g.Id==extra).Duty==GuardDuty.Posted,
                    "Seventh available guard joined a full search.");
                yield return null;
            }
            var wire=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot(),false));
            var restored=new WorldStateStore(0);restored.Restore(wire);
            var saved=restored.GetGuards(roster.SettlementId).Reports.Find(r=>r.Id==report.Id);
            Require(saved.GuardIds.Count==6 && Math.Abs(saved.InvestigationUntil-until)<0.001,"Native serializer lost bounded shared search.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SEARCH_CHAIN_NATIVE_PASS: existingGuards=true; actualActors=6; allRecruitsWalked=true; relayBeyondFirstGuard=true; seventhStaysFree=true; sharedDeadline=true; noInventedSuspect=true; nativeSerializer=true; allArrivedSearching=true; seventhVisibleContact=true");
        }
        private static double Now(){return DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();}
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
