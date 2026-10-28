using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private sealed class WallWatchSite
        {
            internal int BuildingKey, LowerDoorIndex, UpperDoorIndex;
            internal SavedRoutePoint LowerPoint;
            internal List<GuardPoint3> Interior, Wall;
        }
        private sealed class WallDoor
        {
            internal DaggerfallStaticDoors Owner;
            internal StaticDoor Door;
            internal Vector3 Position, Normal;
            internal float Rise;
        }
        private readonly List<WallWatchSite> wallWatchSites = new List<WallWatchSite>();
        private readonly HashSet<DaggerfallRMBBlock> wallWatchBlocks=new HashSet<DaggerfallRMBBlock>();
        private readonly HashSet<int> wallAssignmentFailures=new HashSet<int>();
        private bool wallWatchSurveyed;
        private Coroutine wallWatchWork;
        internal int WallWatchPostCount { get { return wallWatchSites.Count; } }
        private const float WallPostSpacing=20;

        private void UpdateWallWatch(double now)
        {
            if (!OwnsExterior || HauntingShelterNow(now) || wallWatchWork != null || !gateDutySurveyed || gateDutyPairs.Count == 0) return;
            int target = wallWatchSurveyed ? wallWatchSites.Count : 1;
            int active=0;
            foreach(var guard in Roster.Guards)
                if(GuardShifts.OnDuty(guard,now) && guard.Refuge!=null && guard.Refuge.WallRoute!=null)active++;
            if (!wallWatchSurveyed || active < target) wallWatchWork=StartCoroutine(PrepareWallWatch());
        }

        private IEnumerator PrepareWallWatch()
        {
            var roster=Roster;var town=directory;
            Func<bool> valid=()=>OwnsExterior && Roster==roster && directory==town && !GameManager.IsGamePaused && !HauntingShelterNow(Now());
            try
            {
                yield return null;
                if (!wallWatchSurveyed)
                {
                    yield return SurveyWallWatchSites(valid);
                    if (!valid()) yield break;
                    wallWatchSurveyed=true;
                    int target=GuardRoster.TargetWithWallSlots(baseGuardTarget,gateDutyPairs.Count/2,wallWatchSites.Count);
                    if(target>Roster.Target)
                        try {Roster.IncreaseTarget(target,publicPostSites);}
                        catch(ArgumentException) {Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_WALL_STAFF_PENDING: insufficient distinct public relief posts; target="+target);}
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_WATCH_SURVEY: reachableWallPosts="+wallWatchSites.Count+
                        "; spacingMetres="+WallPostSpacing+"; rosterTarget="+Roster.Target);
                }
                yield return AssignReadyWallWatch(valid);
            }
            finally { wallWatchWork=null; }
        }

        // A completed native access route can receive an ordinary guard while
        // later wall sites are still being surveyed. No route or arrival is
        // fabricated, and the same eligibility/assignment path serves both.
        private IEnumerator AssignReadyWallWatch(Func<bool> valid)
        {
            foreach (var site in wallWatchSites)
            {
                    if (!valid()) yield break;
                    var destination=site.Wall[site.Wall.Count-1];
                    if (Roster.Guards.Exists(g=>g.IsAlive && g.Refuge!=null && g.Refuge.WallRoute!=null &&
                        WallPostGap(g.Refuge.WallRoute[g.Refuge.WallRoute.Count-1],destination)<1)) continue;
                    var candidates=Roster.Guards.FindAll(g=>GuardShifts.OnDuty(g,Now()) && g.Duty==GuardDuty.Posted && g.Refuge==null &&
                        g.Interior==null && !Roster.IsOnPatrol(g.Id) && !IsGateGuard(g.Id) &&
                        !patrolTemplates.Exists(p=>p.LeaderId==g.Id || p.FollowerId==g.Id));
                    if (candidates.Count<2) yield break;
                    candidates.Sort((a,b)=>Gap(a.Post,site.LowerPoint).CompareTo(Gap(b.Post,site.LowerPoint)));
                    foreach (var guard in candidates)
                    {
                        var departurePost=guard.Post;var route=new TownGeometry.RouteResult();
                        yield return geometry.ReadRouteSteps(departurePost,site.LowerPoint,route,valid);
                        if (!valid()) yield break;
                        double now=Now();
                        if (route.Points==null || route.Points.Count<2 || !GuardShifts.OnDuty(guard,now) ||
                            guard.Duty!=GuardDuty.Posted || guard.Refuge!=null || IsGateGuard(guard.Id) ||
                            Roster.IsOnPatrol(guard.Id) || !AtPoint(departurePost,guard.Post)) continue;
                        var outside=route.Points.ConvertAll(p=>new GuardPoint3{X=p.X,Z=p.Z});
                        var journey=GuardRefugeJourney.Create(site.BuildingKey,outside,site.Interior,now,1.3,DaggerfallUnity.Instance.WorldTime.TimeScale);
                        journey.WallDuty=true;journey.WallRoute=site.Wall;
                        journey.LowerDoorIndex=site.LowerDoorIndex;journey.UpperDoorIndex=site.UpperDoorIndex;
                        var timing=GuardRefugeJourney.CopyValidated(journey);
                        if (!timing.ReleaseToReturnBy(GuardShifts.NextChange(now))) continue;
                        try
                        {
                            Roster.TakeRefuge(guard.Id,journey,now);
                            guard.Refuge.ReleaseToReturnBy(GuardShifts.NextChange(now));
                            GuardArchery.EquipWallWatch(guard);
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_WATCH_START: guard="+guard.Id+"; shift="+guard.Shift+
                                "; building="+site.BuildingKey+"; lowerDoor="+site.LowerDoorIndex+"; upperDoor="+site.UpperDoorIndex+
                                "; interiorPoints="+site.Interior.Count+"; wallPoints="+site.Wall.Count+"; bow="+guard.HasBow);
                            yield break;
                        }
                        catch (ArgumentException error)
                        {if(wallAssignmentFailures.Add(site.BuildingKey))Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_WALL_ASSIGNMENT_REJECTED: building="+site.BuildingKey+"; reason="+error.Message);}
                    }
            }
        }

        private static double WallPostGap(GuardPoint3 a,GuardPoint3 b)
        {double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return Math.Sqrt(x*x+y*y+z*z);}

        private IEnumerator SurveyWallWatchSites(Func<bool> valid)
        {
            var doors=new List<WallDoor>();
            foreach (var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach (var raw in owner.Doors ?? new StaticDoor[0])
                {
                    BuildingSummary summary;
                    if (raw.doorType!=DoorTypes.Building || !directory.GetBuildingSummary(raw.buildingKey,out summary) ||
                        summary.BuildingType!=DFLocation.BuildingTypes.Town23) continue;
                    var door=raw;door.ownerPosition=owner.transform.position;door.ownerRotation=owner.transform.rotation;
                    var point=DaggerfallStaticDoors.GetDoorPosition(door);var local=directory.transform.InverseTransformPoint(point);
                    doors.Add(new WallDoor{Owner=owner,Door=door,Position=point,Normal=DaggerfallStaticDoors.GetDoorNormal(door),
                        Rise=point.y-geometry.ScenePoint(new RoutePoint(local.x,local.z)).y});
                }
            doors.Sort((a,b)=> {int n=a.Door.buildingKey.CompareTo(b.Door.buildingKey);return n!=0?n:a.Door.doorIndex.CompareTo(b.Door.doorIndex);});
            var surfaces=new List<Vector3>();
            var blocks=new HashSet<DaggerfallRMBBlock>();
            foreach(var door in doors){var block=door.Owner.GetComponentInParent<DaggerfallRMBBlock>();if(block)blocks.Add(block);}
            wallWatchBlocks.UnionWith(blocks);
            var surveyedBlocks=new HashSet<DaggerfallRMBBlock>();
            int paired=0,landingFailures=0,wallFailures=0,stairFailures=0,streetFailures=0;
            foreach (var upper in doors)
            {
                if (!valid()) yield break;
                if(wallWatchSites.Count>=GuardRoster.MaximumWallPosts)break;
                if (upper.Rise<3) continue;
                var lower=doors.Find(d=>d.Rise<3 && d.Owner==upper.Owner && d.Door.buildingKey==upper.Door.buildingKey &&
                    d.Door.blockIndex==upper.Door.blockIndex && d.Door.recordIndex==upper.Door.recordIndex && d.Door.doorIndex!=upper.Door.doorIndex);
                if (lower==null) continue;
                paired++;
                NavMeshHit lowerNav,upperNav;
                if (!DoorLanding(lower,out lowerNav) || !DoorLanding(upper,out upperNav)) {landingFailures++;continue;}
                var local=directory.transform.InverseTransformPoint(lowerNav.position);
                var lowerPoint=new SavedRoutePoint{X=local.x,Z=local.z};
                var approach=new TownGeometry.RouteResult();
                yield return geometry.ReadRouteSteps(gateDutyPosts[0].Point,lowerPoint,approach,valid);
                if(!valid())yield break;
                if(approach.Points==null || approach.Points.Count<2 || RouteCrossesGate(approach.Points)){streetFailures++;continue;}
                // Publish a real local access site without first walking every
                // other wall block's triangle survey. Later blocks use the same
                // geometry checks when their own actual door is reached.
                var block=upper.Owner.GetComponentInParent<DaggerfallRMBBlock>();
                if(block && surveyedBlocks.Add(block))
                    yield return SurveyWallSurfaces(new HashSet<DaggerfallRMBBlock>{block},surfaces,valid);
                if(!valid())yield break;
                var walls=new List<List<GuardPoint3>>();
                yield return FindWallPostRoutes(upperNav.position,surfaces,walls,valid);
                if(walls.Count==0){wallFailures++;continue;}
                var interiorRoute=new List<GuardPoint3>();
                yield return SurveyWallInterior(lower,upper,interiorRoute,valid);
                if (!valid()) yield break;
                if (interiorRoute.Count<2) {stairFailures++;continue;}
                foreach(var wall in walls)
                    wallWatchSites.Add(new WallWatchSite{BuildingKey=lower.Door.buildingKey,LowerDoorIndex=lower.Door.doorIndex,
                        UpperDoorIndex=upper.Door.doorIndex,LowerPoint=lowerPoint,Interior=interiorRoute,Wall=wall});
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_TWO_DOOR_ROUTE: building="+lower.Door.buildingKey+
                    "; lowerDoor="+lower.Door.doorIndex+"; upperDoor="+upper.Door.doorIndex+"; rise="+(upperNav.position.y-lowerNav.position.y)+
                    "; interiorPoints="+interiorRoute.Count+"; wallPosts="+walls.Count);
                yield return AssignReadyWallWatch(valid);
                yield return null;
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_TWO_DOOR_AUDIT: nativeDoors="+doors.Count+"; paired="+paired+
                "; landingFailures="+landingFailures+"; wallPathFailures="+wallFailures+"; interiorStairFailures="+stairFailures+
                "; streetAccessFailures="+streetFailures+"; upperSurfaces="+surfaces.Count+"; ready="+wallWatchSites.Count);
        }

        private static bool DoorLanding(WallDoor door,out NavMeshHit navigation)
        {
            navigation=default(NavMeshHit);RaycastHit floor;
            var outside=door.Position+door.Normal*.8f;
            if (!Physics.Raycast(outside+Vector3.up,Vector3.down,out floor,4,~0,QueryTriggerInteraction.Ignore) || floor.normal.y<.65f) return false;
            return NavMesh.SamplePosition(floor.point,out navigation,.6f,NavMesh.AllAreas) && Math.Abs(navigation.position.y-floor.point.y)<.4f;
        }

        private IEnumerator SurveyWallSurfaces(HashSet<DaggerfallRMBBlock> blocks,List<Vector3> result,Func<bool> valid)
        {
            foreach(var block in blocks)
                foreach(var collider in block.GetComponentsInChildren<MeshCollider>())
                {
                    if(!collider.enabled || !collider.sharedMesh || !collider.sharedMesh.isReadable)continue;
                    var mesh=collider.sharedMesh;var points=mesh.vertices;var indices=mesh.triangles;
                    for(int i=0;i+2<indices.Length;i+=3)
                    {
                        if(i%192==0){yield return null;if(!valid())yield break;}
                        var a=collider.transform.TransformPoint(points[indices[i]]);
                        var b=collider.transform.TransformPoint(points[indices[i+1]]);
                        var c=collider.transform.TransformPoint(points[indices[i+2]]);
                        if(Vector3.Cross(b-a,c-a).normalized.y<.65f)continue;
                        var center=(a+b+c)/3;var local=directory.transform.InverseTransformPoint(center);
                        if(center.y-geometry.ScenePoint(new RoutePoint(local.x,local.z)).y<3)continue;
                        NavMeshHit navigation;
                        if(!NavMesh.SamplePosition(center,out navigation,.5f,NavMesh.AllAreas) || Math.Abs(navigation.position.y-center.y)>.5f)continue;
                        if(!result.Exists(p=>Vector3.Distance(p,navigation.position)<1))result.Add(navigation.position);
                    }
                }
            result.Sort((a,b)=>{int n=a.x.CompareTo(b.x);return n!=0?n:a.z.CompareTo(b.z);});
        }

        private IEnumerator FindWallPostRoutes(Vector3 entrance,List<Vector3> surfaces,List<List<GuardPoint3>> result,Func<bool> valid)
        {
            var candidates=new List<Vector3>(surfaces);
            candidates.Sort((a,b)=>(a-entrance).sqrMagnitude.CompareTo((b-entrance).sqrMagnitude));
            int checkedPoints=0;
            foreach(var destination in candidates)
            {
                if(++checkedPoints%16==0){yield return null;if(!valid())yield break;}
                if(wallWatchSites.Count+result.Count>=GuardRoster.MaximumWallPosts)yield break;
                if(Vector3.Distance(destination,entrance)<4 || Vector3.Distance(destination,entrance)>120)continue;
                var local=directory.transform.InverseTransformPoint(destination);
                var endpoint=new GuardPoint3{X=local.x,Y=local.y,Z=local.z};
                if(wallWatchSites.Exists(s=>WallPostGap(s.Wall[s.Wall.Count-1],endpoint)<WallPostSpacing) ||
                    result.Exists(r=>WallPostGap(r[r.Count-1],endpoint)<WallPostSpacing))continue;
                var path=new NavMeshPath();
                if (!NavMesh.CalculatePath(entrance,destination,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete || path.corners.Length<2) continue;
                float length=0;for(int p=1;p<path.corners.Length;p++)length+=Vector3.Distance(path.corners[p-1],path.corners[p]);
                if(length>150)continue;
                var route=new List<GuardPoint3>();
                foreach(var world in path.corners){local=directory.transform.InverseTransformPoint(world);route.Add(new GuardPoint3{X=local.x,Y=local.y,Z=local.z});}
                result.Add(route);
            }
        }

        private bool RouteCrossesGate(List<SavedRoutePoint> route)
        {
            foreach(var gate in directory.GetComponentsInChildren<DaggerfallCityGate>())
            {
                NativeCityGateNavigation.Aperture aperture;
                if(!NativeCityGateNavigation.TryAperture(gate,geometry.SceneCell(new GridCell(geometry.Width/2,geometry.Height/2)),out aperture))continue;
                for(int i=1;i<route.Count;i++)
                {
                    var a=geometry.ScenePoint(new RoutePoint(route[i-1].X,route[i-1].Z))-aperture.Middle;
                    var b=geometry.ScenePoint(new RoutePoint(route[i].X,route[i].Z))-aperture.Middle;
                    float from=Vector3.Dot(a,aperture.Inward),to=Vector3.Dot(b,aperture.Inward);
                    if(from*to>0 || Math.Abs(from-to)<.0001f)continue;
                    var crossing=Vector3.Lerp(a,b,from/(from-to));
                    if(Math.Abs(Vector3.Dot(crossing,aperture.Axis))<aperture.HalfOpening+.5f)return true;
                }
            }
            return false;
        }

        private static IEnumerator SurveyWallInterior(WallDoor lower,WallDoor upper,List<GuardPoint3> result,Func<bool> valid)
        {
            var temporary=new GameObject("People of the Iliac Bay isolated wall stair route");
            var offset=Vector3.up*100;var shifted=lower.Door;shifted.ownerPosition+=offset;
            try
            {
                var interior=temporary.AddComponent<DaggerfallInterior>();
                var climate=ClimateSwaps.FromAPIClimateBase(GameManager.Instance.PlayerGPS.ClimateSettings.ClimateType);
                if(!interior.DoLayoutAutomap(lower.Owner.transform,shifted,climate))yield break;
                temporary.transform.position=lower.Door.ownerPosition+(Vector3)lower.Door.buildingMatrix.GetColumn(3)+offset;
                temporary.transform.rotation=GameObjectHelper.QuaternionFromMatrix(lower.Door.buildingMatrix);
                foreach(var renderer in temporary.GetComponentsInChildren<Renderer>())renderer.enabled=false;
                Physics.SyncTransforms();yield return null;
                var navigation=new InteriorGeometry(interior,deferSurvey:true);
                while(navigation.AdvanceSurvey()){yield return null;if(!valid())yield break;}
                Vector3 exit,normal;
                if(!navigation.EntryGround.HasValue || !interior.FindClosestInteriorDoor(upper.Position+offset,out exit,out normal) ||
                    Vector3.Distance(exit,upper.Position+offset)>3)yield break;
                var local=interior.transform.InverseTransformPoint(exit+normal*.8f);
                foreach(var approach in navigation.ApproachPoints(local,1.5f,1.5f))
                {
                    var world=interior.transform.TransformPoint(approach);
                    if(world.y-navigation.EntryGround.Value.y<3)continue;
                    var route=navigation.RouteToExit(world);
                    if(route.Count<2)continue;
                    route.Reverse();result.AddRange(route);yield break;
                }
            }
            finally
            {if(temporary){temporary.SetActive(false);UnityEngine.Object.Destroy(temporary);Physics.SyncTransforms();}}
        }

        private void DefendWallWatch(GuardRecord guard,GuardActor actor,double now)
        {
            if(!EnemyDefense || HauntingShelterNow(now) || guard.Refuge==null || guard.Refuge.WallRoute==null ||
                guard.Refuge.Evaluate(now).Phase!=RefugePhase.WallPosted){actor.EndCombat();return;}
            var attacker=actor.WallAttacker;
            if(attacker==GameManager.Instance.PlayerEntityBehaviour &&
                GameManager.Instance.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None)
            {actor.RememberWallAttacker(null);attacker=null;}
            if(attacker && attacker.Entity!=null && attacker.Entity.CurrentHealth>0 && !attacker.Entity.IsInvisible &&
                CivilianResponses.Sees(actor.transform.position,attacker.transform.position,EnemyAttack.maxRangedDistance))
            {actor.DefendWallPost(attacker);return;}
            SurveyWallIntruders(now);
            foreach(var target in wallIntruders)
                if(GuardShifts.WallTrespassEnforced(now,HauntingShelterNow(now)) && WallGuardSees(actor,target) && IsWallIntruder(target))
                {
                    if(target==GameManager.Instance.PlayerEntityBehaviour &&
                        GameManager.Instance.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None)
                        GameManager.Instance.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Trespassing;
                    actor.DefendWallPost(target);return;
                }
            foreach(var target in defenseEnemies.Values)
            {
                if(!IsDefenseTarget(target,now) || target.Entity.IsInvisible || !CivilianResponses.Sees(actor.transform.position,target.transform.position,20))continue;
                var evidence=target.GetComponent<GuardAttackEvidence>();
                if(evidence && evidence.ResidentAssault && (!evidence.Victim || !CivilianResponses.Sees(actor.transform.position,evidence.Victim.transform.position,20)))continue;
                actor.DefendWallPost(target);return;
            }
            actor.EndCombat();
        }
    }
}
