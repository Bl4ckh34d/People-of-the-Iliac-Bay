using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class GateGuardProbe
    {
        private static double Now()
        {
            var now=DaggerfallUnity.Instance.WorldTime.Now;
            return (double)now.ToSeconds()+(double)now.Second-Math.Floor(now.Second);
        }
        private static void SetTimeOfDay(double seconds)
        {
            double now=Now();
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)((seconds-now%86400+86400)%86400));
        }
        private static double Distance(Vector3 a,Vector3 b)
        { a.y=0;b.y=0;return Vector3.Distance(a,b); }
        private static double RouteLength(List<SavedRoutePoint> route)
        {
            double length=0;
            for(int i=1;i<route.Count;i++)
            { double x=route[i].X-route[i-1].X,z=route[i].Z-route[i-1].Z; length+=Math.Sqrt(x*x+z*z); }
            return length;
        }
        private static bool At(SavedRoutePoint a,SavedRoutePoint b)
        { double x=a.X-b.X,z=a.Z-b.Z; return x*x+z*z<0.25; }
        private static DaggerfallCityGate[] OrderedGates(BuildingDirectory directory)
        {
            var gates=directory.GetComponentsInChildren<DaggerfallCityGate>();
            Array.Sort(gates,(a,b)=>{
                var pa=directory.transform.InverseTransformPoint(a.transform.position);
                var pb=directory.transform.InverseTransformPoint(b.transform.position);
                int order=pa.x.CompareTo(pb.x); return order!=0 ? order : pa.z.CompareTo(pb.z);
            });
            return gates;
        }
        private static void VerifySites(TownGeometry geometry,DaggerfallCityGate[] gates,
            List<TownGeometry.GateGuardPostSites> sites)
        {
            if(gates.Length==0 || sites.Count!=gates.Length)
                throw new InvalidOperationException("Gate survey did not produce one complete post set for every native gate: gates="+
                    gates.Length+"; surveyed="+sites.Count+".");
            var cityCenter=geometry.SceneCell(new GridCell(geometry.Width/2,geometry.Height/2));
            foreach(var site in sites)
            {
                if(site.GateIndex<0 || site.GateIndex>=gates.Length)
                    throw new InvalidOperationException("Gate post site has invalid native gate index.");
                var gate=gates[site.GateIndex]; var mesh=gate.GetComponent<MeshFilter>();
                if(!mesh || !mesh.sharedMesh) throw new InvalidOperationException("Native gate has no survey mesh.");
                var bounds=mesh.sharedMesh.bounds;
                bool normalAlongX=bounds.size.x<bounds.size.z;
                var normal=gate.transform.TransformDirection(normalAlongX ? Vector3.right : Vector3.forward); normal.y=0;
                var apertureCenter=bounds.center;
                bool nativeFrame=Mathf.Abs(bounds.size.x-25.6f)<0.05f && Mathf.Abs(bounds.size.z-9.6f)<0.05f;
                if(nativeFrame) apertureCenter.x=(-298+43)*0.025f/2;
                var middle=gate.transform.TransformPoint(apertureCenter);
                if(Vector3.Dot(normal,cityCenter-middle)<0) normal=-normal;
                normal.Normalize();
                var axis=gate.transform.TransformDirection(normalAlongX ? Vector3.forward : Vector3.right); axis.y=0; axis.Normalize();
                var inLeft=geometry.ScenePoint(new RoutePoint(site.InsideLeft.X,site.InsideLeft.Z));
                var inRight=geometry.ScenePoint(new RoutePoint(site.InsideRight.X,site.InsideRight.Z));
                var returnLeft=geometry.ScenePoint(new RoutePoint(site.InsideReturnLeft.X,site.InsideReturnLeft.Z));
                var returnRight=geometry.ScenePoint(new RoutePoint(site.InsideReturnRight.X,site.InsideReturnRight.Z));
                var outLeft=geometry.ScenePoint(new RoutePoint(site.OutsideLeft.X,site.OutsideLeft.Z));
                var outRight=geometry.ScenePoint(new RoutePoint(site.OutsideRight.X,site.OutsideRight.Z));
                if(Vector3.Dot(inLeft-middle,normal)<0.8f || Vector3.Dot(inRight-middle,normal)<0.8f ||
                    Vector3.Dot(returnLeft-middle,normal)<0.8f || Vector3.Dot(returnRight-middle,normal)<0.8f ||
                    Vector3.Dot(outLeft-middle,normal)>-0.8f || Vector3.Dot(outRight-middle,normal)>-0.8f)
                    throw new InvalidOperationException("Gate "+site.GateIndex+" post signs do not place inner and outer sentries on opposite wall sides.");
                if(Vector3.Dot(inLeft-middle,axis)>=-0.5f || Vector3.Dot(inRight-middle,axis)<=0.5f ||
                    Vector3.Dot(outLeft-middle,axis)>=-0.5f || Vector3.Dot(outRight-middle,axis)<=0.5f)
                    throw new InvalidOperationException("Gate "+site.GateIndex+" left/right posts do not flank opening.");
                if(Vector3.Dot(site.InsideFacing,normal)<0.95f || Vector3.Dot(site.OutsideFacing,normal)>-0.95f)
                    throw new InvalidOperationException("Gate "+site.GateIndex+" post facing does not point away from wall.");
                if(nativeFrame)
                    foreach(var position in new[] { inLeft,inRight,outLeft,outRight })
                        if(Mathf.Abs(Vector3.Dot(position-middle,axis))>9)
                            throw new InvalidOperationException("Gate sentry is beside the wall frame instead of the doorway.");
                var positions=new[] { site.InsideLeft,site.InsideRight,site.InsideReturnLeft,site.InsideReturnRight,
                    site.OutsideLeft,site.OutsideRight };
                for(int i=0;i<positions.Length;i++) for(int j=i+1;j<positions.Length;j++)
                {
                    double dx=positions[i].X-positions[j].X,dz=positions[i].Z-positions[j].Z;
                    if(dx*dx+dz*dz<2.25) throw new InvalidOperationException("Gate "+site.GateIndex+" has overlapping guard stations.");
                }
                foreach(var pair in new[] {
                    new[] { site.OutsideLeft,site.InsideReturnLeft },new[] { site.OutsideRight,site.InsideReturnRight } })
                {
                    var route=geometry.GateRouteBetweenPoints(pair[0],pair[1]);
                    if(route.Count<2 || Distance(geometry.ScenePoint(new RoutePoint(route[0].X,route[0].Z)),
                        geometry.ScenePoint(new RoutePoint(pair[0].X,pair[0].Z)))>0.2 ||
                        Distance(geometry.ScenePoint(new RoutePoint(route[route.Count-1].X,route[route.Count-1].Z)),
                        geometry.ScenePoint(new RoutePoint(pair[1].X,pair[1].Z)))>0.2)
                        throw new InvalidOperationException("Gate "+site.GateIndex+" exterior guard lacks a surveyed physical return route while open.");
                }
            }
        }
        private static List<GateGuardDutySnapshot> Snapshots(GuardPopulation guards)
        { return guards.GateGuardDutySnapshots(Now()); }
        private static bool HasNoonAssignments(GuardPopulation guards,List<TownGeometry.GateGuardPostSites> sites)
        {
            var states=Snapshots(guards);
            foreach(var site in sites)
            {
                var group=states.FindAll(s=>s.GateIndex==site.GateIndex);
                if(group.Count!=4) return false;
                foreach(var state in group)
                    if(state.GuardId==null || !state.GateOpen || state.Duty!=GuardDuty.Posted ||
                        !guards.IsGateGuard(state.GuardId) || guards.Roster.IsOnPatrol(state.GuardId) ||
                        !At(state.Position,state.Target)) return false;
                if(group.FindAll(s=>!s.ExteriorAssignment).Count!=2 || group.FindAll(s=>s.ExteriorAssignment).Count!=2) return false;
                var innerLeft=group.Find(s=>!s.ExteriorAssignment && s.Side==0);
                var innerRight=group.Find(s=>!s.ExteriorAssignment && s.Side==1);
                var outerLeft=group.Find(s=>s.ExteriorAssignment && s.Side==0);
                var outerRight=group.Find(s=>s.ExteriorAssignment && s.Side==1);
                if(innerLeft==null || innerRight==null || outerLeft==null || outerRight==null ||
                    !At(innerLeft.Target,site.InsideLeft) || !At(innerRight.Target,site.InsideRight) ||
                    !At(outerLeft.Target,site.OutsideLeft) || !At(outerRight.Target,site.OutsideRight)) return false;
                if(new HashSet<string> { innerLeft.GuardId,innerRight.GuardId,outerLeft.GuardId,outerRight.GuardId }.Count!=4) return false;
            }
            return true;
        }
        private static void VerifyQuota(GuardPopulation guards, int gates)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var host = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var population = (SettlementPopulation)typeof(LivingCitiesMod).GetField("current", flags).GetValue(host);
            int residents = 0;
            foreach (var home in population.Households) residents += home.Residents.Count;
            int ordinary = GuardRoster.TargetCount(SettlementClassification.FromLocationType(
                (int)GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.LocationType), residents);
            int target = GuardRoster.TargetWithGateSlots(ordinary, gates);
            var roster = guards.Roster;
            int gateGuards = roster.Guards.FindAll(g => g.IsAlive && guards.IsGateGuard(g.Id)).Count;
            int townGuards = roster.Guards.FindAll(g => g.IsAlive && !guards.IsGateGuard(g.Id)).Count;
            if (roster.Target != target || gateGuards != gates * 4 || townGuards != ordinary)
                throw new InvalidOperationException("Guard quota mismatch: target=" + roster.Target + "; ordinary=" + townGuards + "; gates=" + gateGuards);
            var json = DaggerfallWorkshop.Game.Serialization.SaveLoadManager.Serialize(typeof(GuardRoster), roster);
            var restored = GuardRoster.CopyValidated((GuardRoster)DaggerfallWorkshop.Game.Serialization.SaveLoadManager.Deserialize(typeof(GuardRoster), json));
            if (restored.Target != target || restored.Guards.Count != roster.Guards.Count ||
                restored.IncreaseTarget(target, new List<SavedRoutePoint>()) != 0)
                throw new InvalidOperationException("Native guard quota restore grew the roster.");
            Debug.Log("LIVING_CITIES_GUARD_QUOTA_PASS: residents=" + residents + "; ordinary=" + townGuards +
                "; gateGuards=" + gateGuards + "; target=" + target + "; native restore remains idempotent.");
        }
        public static IEnumerator Run(GuardPopulation guards)
        {
            if(!guards) guards=UnityEngine.Object.FindObjectOfType<GuardPopulation>();
            if(!guards) throw new InvalidOperationException("Gate guard probe requires live GuardPopulation controller.");
            var game=GameManager.Instance;
            var savedPlayer=game.PlayerMotor.transform.position;
            var savedCrime=game.PlayerEntity.CrimeCommitted;
            game.PlayerEntity.CrimeCommitted=DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
            try
            {
                float readyDeadline=Time.realtimeSinceStartup+90;
                while((!guards.OwnsSettlement || guards.Roster==null ||
                    guards.Roster.SettlementId!=game.PlayerGPS.CurrentLocation.MapTableData.MapId) &&
                    Time.realtimeSinceStartup<readyDeadline) yield return null;
                SetTimeOfDay(12*3600);
                for(int i=0;i<3;i++) yield return null;
                var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                var navigation=game.StreamingWorld.GetCurrentCityNavigation();
                if(!directory || !navigation || !guards.OwnsSettlement)
                    throw new InvalidOperationException("Gate probe is not in an owned loaded city exterior.");
                var gates=OrderedGates(directory);
                var geometry=new TownGeometry(directory,navigation,false);
                var expectedPosts=geometry.SurveyGuardPosts(GuardRoster.MaximumTarget);
                var postResult=new TownGeometry.GuardPostResult();
                var postSteps=geometry.SurveyGuardPostSteps(GuardRoster.MaximumTarget,postResult);
                int preparationSlices=0;
                while(postSteps.MoveNext())
                {
                    if(postResult.Posts!=null) throw new InvalidOperationException("Partial guard posts were published.");
                    preparationSlices++; yield return null;
                }
                if(postResult.Posts==null || postResult.Posts.Count!=expectedPosts.Count)
                    throw new InvalidOperationException("Sliced guard post count differs.");
                for(int p=0;p<expectedPosts.Count;p++)
                    if(!At(expectedPosts[p],postResult.Posts[p])) throw new InvalidOperationException("Sliced guard post selection differs.");
                var expectedEntrances=new TownGeometry(directory,navigation);
                var slicedEntrances=new TownGeometry(directory,navigation,false);
                var entranceSteps=slicedEntrances.ReadEntranceSteps();
                while(entranceSteps.MoveNext())
                {
                    if(slicedEntrances.Entrances.Count!=0) throw new InvalidOperationException("Partial entrances were published.");
                    preparationSlices++; yield return null;
                }
                if(expectedEntrances.Entrances.Count!=slicedEntrances.Entrances.Count || preparationSlices<2)
                    throw new InvalidOperationException("Sliced guard entrance coverage differs.");
                foreach(var pair in expectedEntrances.Entrances)
                {
                    TownGeometry.Entrance actual;
                    if(!slicedEntrances.Entrances.TryGetValue(pair.Key,out actual) || !actual.Cell.Equals(pair.Value.Cell) ||
                        Vector3.Distance(actual.LocalDoor,pair.Value.LocalDoor)>0.001f)
                        throw new InvalidOperationException("Sliced guard entrance differs.");
                }
                var cancelledPosts=new TownGeometry.GuardPostResult();
                var cancelledSteps=geometry.SurveyGuardPostSteps(GuardRoster.MaximumTarget,cancelledPosts);
                cancelledSteps.MoveNext(); (cancelledSteps as IDisposable)?.Dispose();
                if(cancelledPosts.Posts!=null) throw new InvalidOperationException("Cancelled guard survey published partial posts.");
                Debug.Log("LIVING_CITIES_GUARD_SETUP_SLICES_PASS: posts="+expectedPosts.Count+"; entrances="+expectedEntrances.Entrances.Count+"; slices="+preparationSlices+"; cancellation leaves no partial posts.");
                var sites=geometry.SurveyGateGuardPosts();
                VerifySites(geometry,gates,sites);
                float assignmentDeadline=Time.realtimeSinceStartup+180;
                float assignmentDiagnostic=Time.realtimeSinceStartup+5;
                bool advancedToArrival=false;
                while(!HasNoonAssignments(guards,sites) && Time.realtimeSinceStartup<assignmentDeadline)
                {
                    var assigned=Snapshots(guards);
                    if(!advancedToArrival && assigned.Count==sites.Count*4 && assigned.TrueForAll(s=>s.GuardId!=null))
                    {
                        double latest=Now();
                        foreach(var state in assigned) latest=Math.Max(latest,state.ArrivesAt);
                        double arrivalDusk=Math.Floor(Now()/86400)*86400+DaggerfallDateTime.DuskHour*3600;
                        if(latest+600>=arrivalDusk) throw new InvalidOperationException("Initial gate assignments have no daytime arrival window.");
                        DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(latest+1-Now()));
                        advancedToArrival=true;
                    }
                    if(Time.realtimeSinceStartup>=assignmentDiagnostic)
                    {
                        assignmentDiagnostic=Time.realtimeSinceStartup+30;
                        Debug.Log("LIVING_CITIES_GATE_WAIT: now="+Now()+"; paused="+GameManager.IsGamePaused+
                            "; window="+(DaggerfallUI.UIManager.TopWindow==null?"none":DaggerfallUI.UIManager.TopWindow.GetType().Name));
                        foreach(var state in Snapshots(guards))
                            Debug.Log("LIVING_CITIES_GATE_WAIT_GUARD: "+state.GuardId+"; duty="+state.Duty+"; arrives="+state.ArrivesAt+
                                "; at="+At(state.Position,state.Target)+"; open="+state.GateOpen+"; patrol="+guards.Roster.IsOnPatrol(state.GuardId));
                    }
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                if(!HasNoonAssignments(guards,sites))
                    throw new InvalidOperationException("Finite garrison did not reach four distinct assigned gate posts per gate; assigned="+
                        guards.GateGuardDutySnapshots(Now()).Count+"; required="+(sites.Count*4)+".");

                VerifyQuota(guards, sites.Count);
                var first=sites[0];
                var left=geometry.ScenePoint(new RoutePoint(first.InsideLeft.X,first.InsideLeft.Z));
                var right=geometry.ScenePoint(new RoutePoint(first.InsideRight.X,first.InsideRight.Z));
                var middle=(left+right)*0.5f;
                game.PlayerMotor.transform.position=middle+first.InsideFacing*10+Vector3.up;
                SleepProbe.AimCamera(middle+Vector3.up);
                yield return new WaitForSecondsRealtime(0.5f);
                foreach(var state in Snapshots(guards).FindAll(s=>s.GateIndex==first.GateIndex))
                {
                    GuardActor actor=null;
                    foreach(var view in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                        if(view.GuardId==state.GuardId) { actor=view; break; }
                    var expected=geometry.ScenePoint(new RoutePoint(state.Position.X,state.Position.Z));
                    var facing=state.ExteriorAssignment ? first.OutsideFacing : first.InsideFacing;
                    if(!actor || Distance(actor.transform.position,expected)>0.5 || Vector3.Dot(actor.transform.forward,facing)<0.95f)
                        throw new InvalidOperationException("Rendered gate guard does not match stationed position/facing: "+state.GuardId);
                }
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"LivingCities-gate-posts.png"));
                Debug.Log("LIVING_CITIES_GATE_GUARD_VIEW_PASS: four actual gate actors match assigned posts and facing.");
                if(Array.IndexOf(Environment.GetCommandLineArgs(), "-livingcities-guard-quota-only")>=0) yield break;

                // Keep this day's armed recalls. Advancing to next noon would correctly
                // complete them offscreen and skip the movement interval under test.
                double dusk=Math.Floor(Now()/86400)*86400+DaggerfallDateTime.DuskHour*3600;
                double shortestReturn=double.PositiveInfinity, longestReturn=0;
                foreach(var site in sites)
                foreach(var pair in new[] {
                    new[] { site.OutsideLeft,site.InsideReturnLeft },new[] { site.OutsideRight,site.InsideReturnRight } })
                {
                    var route=geometry.GateRouteBetweenPoints(pair[0],pair[1]);
                    double travel=RouteLength(route)*DaggerfallUnity.Instance.WorldTime.TimeScale/1.3;
                    var sliced=new TownGeometry.RouteResult();int routeFrame=Time.frameCount;
                    yield return geometry.ReadGateRouteSteps(pair[0],pair[1],sliced,()=>true);
                    if(sliced.Points==null || sliced.Points.Count!=route.Count || Time.frameCount<=routeFrame)
                        throw new InvalidOperationException("Sliced gate route differs or did not yield.");
                    for(int point=0;point<route.Count;point++)
                        if(!At(route[point],sliced.Points[point])) throw new InvalidOperationException("Sliced gate route changed geometry.");
                    var cancelled=new TownGeometry.RouteResult();
                    yield return geometry.ReadGateRouteSteps(pair[0],pair[1],cancelled,()=>false);
                    if(cancelled.Points!=null) throw new InvalidOperationException("Cancelled gate route published points.");
                    shortestReturn=Math.Min(shortestReturn,travel); longestReturn=Math.Max(longestReturn,travel);
                }
                double startAt=dusk-longestReturn-120;
                Debug.Log("LIVING_CITIES_GATE_ROUTE_SLICES_PASS: all native gate return paths match synchronous geometry; cancellation publishes nothing.");
                if(startAt<=Now()) throw new InvalidOperationException("Gate return routes leave no daytime window for a pre-close test.");
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(startAt-Now()));
                float closeDeadline=Time.realtimeSinceStartup+(float)((longestReturn+180)/DaggerfallUnity.Instance.WorldTime.TimeScale+20);
                bool sawPhysicalMovement=false;
                while(Time.realtimeSinceStartup<closeDeadline)
                {
                    var states=Snapshots(guards);
                    bool allInside=true;
                    foreach(var site in sites)
                    foreach(var side in new[] { 0,1 })
                    {
                        var state=states.Find(s=>s.GateIndex==site.GateIndex && s.ExteriorAssignment && s.Side==side);
                        var outside=side==0 ? site.OutsideLeft : site.OutsideRight;
                        var inside=side==0 ? site.InsideReturnLeft : site.InsideReturnRight;
                        if(state==null || !state.GateOpen)
                            throw new InvalidOperationException("Native gate closed before exterior sentries reached inner posts.");
                        if(!At(state.Target,inside)) allInside=false;
                        if((state.Duty==GuardDuty.Returning || state.Duty==GuardDuty.Refuge) && At(state.Target,inside))
                        {
                            var source=geometry.ScenePoint(new RoutePoint(outside.X,outside.Z));
                            var target=geometry.ScenePoint(new RoutePoint(inside.X,inside.Z));
                            var position=geometry.ScenePoint(new RoutePoint(state.Position.X,state.Position.Z));
                            if(Distance(position,source)>0.1 && Distance(position,target)>0.1) sawPhysicalMovement=true;
                        }
                        var recalled=guards.Roster.Guards.Find(g=>g.Id==state.GuardId);
                        bool crossed=recalled.Refuge!=null && recalled.Refuge.WallDuty && Now()>=recalled.ArrivesAt;
                        if(!crossed && (state.Duty!=GuardDuty.Posted || !At(state.Position,inside))) allInside=false;
                    }
                    if(allInside) break;
                    yield return null;
                }
                if(!sawPhysicalMovement)
                    throw new InvalidOperationException("Gate duty changed target without a rendered physical return journey.");
                var final=Snapshots(guards);
                foreach(var state in final)
                    if(state.ExteriorAssignment && (state.Duty!=GuardDuty.Refuge || state.GateOpen==false))
                        throw new InvalidOperationException("Exterior sentry failed to finish inside before native gate closure: "+state.GuardId+".");
                if(Now()>=dusk || DaggerfallUnity.Instance.WorldTime.Now.IsNight)
                    throw new InvalidOperationException("Gate return completed at or after native dusk.");
                Debug.Log("LIVING_CITIES_GATE_GUARD_PASS: gates="+gates.Length+"; four finite guards per gate; inner/outer signs and facing; " +
                    "patrol exclusion; outer guards physically returned before 18:00; maxRouteGameSeconds="+longestReturn+
                    "; minRouteGameSeconds="+shortestReturn+".");

                // A second day crosses dusk entirely indoors. Exterior physics must not be needed.
                SetTimeOfDay(12*3600);
                assignmentDeadline=Time.realtimeSinceStartup+180;
                while(!HasNoonAssignments(guards,sites) && Time.realtimeSinceStartup<assignmentDeadline)
                    yield return new WaitForSecondsRealtime(0.5f);
                if(!HasNoonAssignments(guards,sites)) throw new InvalidOperationException("Gate guards failed to redeploy next day.");
                var recalls=new Dictionary<string,SavedRoutePoint>();
                foreach(var state in Snapshots(guards))
                    if(state.ExteriorAssignment)
                    {
                        var guard=guards.Roster.Guards.Find(g=>g.Id==state.GuardId);
                        if(guard.PostRecall==null || guard.PostRecall.WallDuty==null) throw new InvalidOperationException("Exterior gate guard has no persisted wall shift.");
                        recalls.Add(guard.Id,guard.PostRecall.Route[guard.PostRecall.Route.Count-1]);
                    }
                DaggerfallStaticDoors innDoor=null; StaticDoor entry=default(StaticDoor);
                foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach(var door in owner.Doors ?? new StaticDoor[0])
                    {
                        BuildingSummary building;
                        if(door.doorType==DoorTypes.Building && directory.GetBuildingSummary(door.buildingKey,out building) &&
                            building.BuildingType==DaggerfallConnect.DFLocation.BuildingTypes.Tavern)
                        { innDoor=owner; entry=door; break; }
                    }
                if(!innDoor) throw new InvalidOperationException("Gate recall probe needs a real inn.");
                TownSmokeHarness.EnterInterior(innDoor.transform,entry);
                yield return new WaitForSecondsRealtime(1);
                if(!game.IsPlayerInside) throw new InvalidOperationException("Gate recall test did not enter the inn.");
                SetTimeOfDay(22*3600);
                yield return new WaitForSecondsRealtime(1);
                foreach(var pair in recalls)
                {
                    var guard=guards.Roster.Guards.Find(g=>g.Id==pair.Key);
                    if(guard.Duty!=GuardDuty.Refuge || guard.Refuge==null || !guard.Refuge.WallDuty ||
                        guard.Refuge.Evaluate(Now()).Phase!=RefugePhase.Sheltered || !At(guard.Post,pair.Value) || guard.PostRecall!=null)
                        throw new InvalidOperationException("Guard missed gate recall while player stayed indoors: "+pair.Key);
                }
                Debug.Log("LIVING_CITIES_GATE_INDOOR_RECALL_PASS: eight persisted recalls completed across dusk inside a native inn.");
                game.PlayerEnterExit.TransitionExterior();
                yield return new WaitForSecondsRealtime(1);
                var wallGuard=guards.Roster.Guards.Find(g=>g.Refuge!=null && g.Refuge.WallDuty);
                DaggerfallStaticDoors wallOwner=null; StaticDoor wallDoor=default(StaticDoor);
                foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach(var door in owner.Doors ?? new StaticDoor[0])
                        if(door.doorType==DoorTypes.Building && door.buildingKey==wallGuard.Refuge.BuildingKey)
                        {wallOwner=owner;wallDoor=door;}
                if(!wallOwner) throw new InvalidOperationException("Reserved wall quarter is missing.");
                yield return VerifySlicedWallSurvey(wallOwner.transform,wallDoor);
                TownSmokeHarness.EnterInterior(wallOwner.transform,wallDoor);
                yield return new WaitForSecondsRealtime(1);
                GuardActor wallActor=null;
                foreach(var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                    if(actor.GuardId==wallGuard.Id && actor.IsInterior) wallActor=actor;
                if(!wallActor) throw new InvalidOperationException("Off-duty guard is absent from assigned wall room.");
                var previous=wallActor.transform.position;
                yield return new WaitForSecondsRealtime(2);
                if(Vector3.Distance(previous,wallActor.transform.position)<0.1f)
                    throw new InvalidOperationException("Off-duty wall guard is not physically patrolling.");
                var wallInterior=game.PlayerEnterExit.Interior;
                var wallNavigation=new InteriorGeometry(wallInterior);
                bool cameraPlaced=false;
                foreach(var point in wallGuard.Refuge.Interior)
                {
                    var ground=wallInterior.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z));
                    float gap=Vector3.Distance(ground+Vector3.up,wallActor.transform.position);
                    if(gap<1.7f || gap>3 || !wallNavigation.HasClearLine(ground+Vector3.up*1.6f,wallActor.transform.position)) continue;
                    game.PlayerMotor.transform.position=ground+Vector3.up;
                    cameraPlaced=true; break;
                }
                if(!cameraPlaced) throw new InvalidOperationException("No visible camera position beside patrolling wall guard.");
                yield return null;
                SleepProbe.AimCamera(wallActor.transform.position);
                // Native mobile billboards orient in Update. Let them process the
                // newly aimed camera before capturing; otherwise a quad can be edge-on.
                yield return new WaitForSecondsRealtime(0.2f);
                yield return new WaitForEndOfFrame();
                Debug.Log("LIVING_CITIES_WALL_VIEW: camera="+Camera.main.transform.position.ToString("F3")+
                    "; guard="+wallActor.transform.position.ToString("F3")+
                    "; viewport="+Camera.main.WorldToViewportPoint(wallActor.transform.position).ToString("F3"));
                foreach(var renderer in wallActor.GetComponentsInChildren<Renderer>())
                    Debug.Log("LIVING_CITIES_WALL_RENDERER: "+renderer.name+"; enabled="+renderer.enabled+
                        "; active="+renderer.gameObject.activeInHierarchy+"; center="+renderer.bounds.center.ToString("F3"));
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"LivingCities-wall-duty.png"));
                // CaptureScreenshot writes at the next rendered frame. Advancing
                // to dawn immediately removes the actor before that frame exists.
                yield return new WaitForSecondsRealtime(0.5f);
                SetTimeOfDay(8*3600);
                yield return new WaitForSecondsRealtime(1);
                foreach(var pair in recalls)
                {
                    var guard=guards.Roster.Guards.Find(g=>g.Id==pair.Key);
                    if(guard.Refuge!=null || guard.Duty!=GuardDuty.Posted || !At(guard.Post,pair.Value))
                        throw new InvalidOperationException("Off-duty guard did not finish morning return.");
                }
                Debug.Log("LIVING_CITIES_WALL_DUTY_PASS: eight unique wall shifts; indoor time advance, visible patrol and morning return.");
                game.PlayerEnterExit.TransitionExterior();
                yield return new WaitForSecondsRealtime(1);
            }
            finally
            {
                game.PlayerMotor.transform.position=savedPlayer;
                game.PlayerEntity.CrimeCommitted=savedCrime;
            }
        }
        private static IEnumerator VerifySlicedWallSurvey(Transform owner,StaticDoor door)
        {
            var expected=RefugeGeometry.Survey(owner,door);
            var actual=new List<List<GuardPoint3>>();
            var operation=RefugeGeometry.SurveySteps(owner,door,actual);
            int slices=0;
            try { while(operation.MoveNext()) { slices++; yield return null; } }
            finally { (operation as IDisposable)?.Dispose(); }
            if(slices<2 || actual.Count!=expected.Count) throw new InvalidOperationException("Sliced wall survey differs from synchronous survey.");
            for(int r=0;r<expected.Count;r++)
            {
                if(actual[r].Count!=expected[r].Count) throw new InvalidOperationException("Sliced wall route length differs.");
                for(int p=0;p<expected[r].Count;p++)
                {
                    var a=actual[r][p]; var b=expected[r][p];
                    if(Math.Abs(a.X-b.X)+Math.Abs(a.Y-b.Y)+Math.Abs(a.Z-b.Z)>0.001)
                        throw new InvalidOperationException("Sliced wall route geometry differs.");
                }
            }
            // Dispose while model colliders exist, before results are published.
            var canceled=RefugeGeometry.SurveySteps(owner,door,new List<List<GuardPoint3>>());
            try { if(!canceled.MoveNext()) throw new InvalidOperationException("Wall cancellation fixture did not yield."); }
            finally { (canceled as IDisposable)?.Dispose(); }
            yield return null;
            foreach(var room in UnityEngine.Object.FindObjectsOfType<DaggerfallInterior>())
                if(room.name=="Living Cities isolated refuge geometry")
                    throw new InvalidOperationException("Canceled wall survey leaked model colliders.");
            Debug.Log("LIVING_CITIES_WALL_SLICES_PASS: routes="+actual.Count+"; slices="+slices+"; cancellation disposed model.");
        }
    }
}
