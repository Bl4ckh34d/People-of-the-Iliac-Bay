using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class OutdoorNavigationTests
    {
        internal static void Add(List<(string,Action)> cases)
        {
            cases.Add(("Legacy personal routes stay executable without a second offset while awaiting safe migration",()=>{
                var source=new ResidentRoutine {ResidentId="old-person",RouteGeometryVersion=2,PersonalLaneVersion=1,
                    Route=new List<SavedRoutePoint>{P(0,0),P(2,3)},
                    Itinerary=new ResidentDestination {Stops=new List<ResidentVisitStop>{new ResidentVisitStop {
                        BuildingKey=2,Purpose=ResidentVisitPurpose.Social,DwellSeconds=600,
                        Route=new List<SavedRoutePoint>{P(0,0),P(2,3)},WalkSeconds=7}},
                        ExitRoute=new List<SavedRoutePoint>{P(2,3),P(0,0)},ExitWalkSeconds=7}};
                int calls=0;
                var route=OutdoorRoutineMigration.RuntimeRoute(source,r=>{calls++;return r;});
                var itinerary=OutdoorRoutineMigration.RuntimeItinerary(source,r=>{calls++;return r;});
                Check.Equal(0,calls);Check.Near(3,route[1].Z);Check.Near(7,itinerary.Stops[0].WalkSeconds);
                Check.True(OutdoorRoutineMigration.NeedsCenterlineRebuild(source));
            }));
            cases.Add(("Prior personal route version rebuilds saved walk times for the current pace",()=>{
                var route=new List<SavedRoutePoint>{P(0,0),P(4,0)};
                double oldWalkSeconds=4*2/ResidentMovement.WalkSpeed;
                var source=new ResidentRoutine {ResidentId="person",DestinationKey=2,RouteGeometryVersion=2,
                    PersonalLaneVersion=2,Route=route,Itinerary=new ResidentDestination {
                        Stops=new List<ResidentVisitStop>{new ResidentVisitStop {BuildingKey=2,
                            Purpose=ResidentVisitPurpose.Social,Route=route,WalkSeconds=oldWalkSeconds,DwellSeconds=600}},
                        ExitRoute=new List<SavedRoutePoint>{P(4,0),P(0,0)},ExitWalkSeconds=oldWalkSeconds}};
                Check.True(OutdoorRoutineMigration.NeedsCenterlineRebuild(source));
                var updated=OutdoorRoutineMigration.Rebuild(source,new List<List<SavedRoutePoint>>{route,
                    new List<SavedRoutePoint>{P(4,0),P(0,0)}},ResidentMovement.MaximumWalkSpeed,2,true);
                Check.Equal(OutdoorRoutineMigration.CurrentPersonalLaneVersion,updated.PersonalLaneVersion);
                Check.Near(4*2/ResidentMovement.MaximumWalkSpeed,updated.Itinerary.Stops[0].WalkSeconds);
                Check.Near(oldWalkSeconds,source.Itinerary.Stops[0].WalkSeconds);
            }));
            cases.Add(("Personal lane soft turns preserve endpoints and bound sweep callbacks",()=>{
                var route=new List<SavedRoutePoint>{P(-1,0),P(0,0),P(6,0),P(6,6),P(12,6),P(13,6)};
                int calls=0;
                var operation=OutdoorRouteSmoother.BeginRounding(route,(a,b)=>{calls++;return true;});
                while(!operation.IsComplete) {int before=calls;operation.Step(2);Check.True(calls-before<=2);}
                var result=operation.Points;
                Check.Near(-1,result[0].X);Check.Near(13,result[result.Count-1].X);
                Check.True(result.Count>route.Count);
                for(int i=2;i<result.Count-2;i++) {
                    double ax=result[i].X-result[i-1].X,az=result[i].Z-result[i-1].Z;
                    double bx=result[i+1].X-result[i].X,bz=result[i+1].Z-result[i].Z;
                    double lengths=Math.Sqrt((ax*ax+az*az)*(bx*bx+bz*bz));
                    if(lengths>0.000001) Check.True((ax*bx+az*bz)/lengths>0.8,"Final lane retained a sharp turn.");
                }
            }));
            cases.Add(("Changing street widths cannot introduce sideways lane spikes",()=>{
                var line=new List<RoutePoint>();for(int i=0;i<=30;i++) line.Add(new RoutePoint(i*1.6,0));
                // The saved Daggerfall temple leg changed by 5.73 m laterally
                // over 1.6 m of street after independent cross-section selection.
                var lane=CorridorLaneRoutes.Offset(line,27,(p,n)=>
                    p.X>=16 && p.X<=19.2 ? new LaneCrossSection(0,6) : new LaneCrossSection(0,0),(a,b)=>true);
                for(int i=1;i<lane.Count;i++)
                    Check.True(Math.Abs(lane[i].Z-lane[i-1].Z)<=0.6*(lane[i].X-lane[i-1].X)+0.00001,
                        "Changing road width created an abrupt sideways step.");
            }));
            cases.Add(("Collision lane contraction propagates gently into neighbouring controls",()=>{
                var line=new List<RoutePoint>();for(int i=0;i<=30;i++) line.Add(new RoutePoint(i*1.6,0));
                bool Clear(RoutePoint a,RoutePoint b) {
                    for(int i=0;i<=20;i++) {double t=i/20d,x=a.X+(b.X-a.X)*t,z=a.Z+(b.Z-a.Z)*t;
                        if(x>=23.9 && x<=24.1 && Math.Abs(z)>0.05) return false;}
                    return true;
                }
                var lane=CorridorLaneRoutes.Offset(line,27,(p,n)=>new LaneCrossSection(0,6),Clear);
                for(int i=1;i<lane.Count;i++) {
                    Check.True(Clear(lane[i-1],lane[i]));
                    Check.True(Math.Abs(lane[i].Z-lane[i-1].Z)<=0.6*(lane[i].X-lane[i-1].X)+0.00001,
                        "Collision contraction created a new sideways kink.");
                }
                Check.True(lane.Exists(p=>p.X>8 && p.X<16 && Math.Abs(p.Z)>2),"Local obstruction collapsed the entire lane.");
            }));
            cases.Add(("Personal lane geometry is persisted and never offset again when scene cache is empty",()=>{
                var source=new ResidentRoutine {ResidentId="person",DestinationKey=2,OpenMinute=0,CloseMinute=1440,
                    Route=new List<SavedRoutePoint>{P(0,0),P(3,4)},ItineraryDepartureSeconds=30000,
                    Itinerary=new ResidentDestination {Stops=new List<ResidentVisitStop>{new ResidentVisitStop {
                        BuildingKey=2,Purpose=ResidentVisitPurpose.Social,WalkSeconds=8,DwellSeconds=600,
                        Route=new List<SavedRoutePoint>{P(0,0),P(3,4)}}},
                        ExitRoute=new List<SavedRoutePoint>{P(3,4),P(0,0)},ExitWalkSeconds=8}};
                var personal=new List<SavedRoutePoint>{P(0,0),P(0,3),P(3,3),P(3,4)};
                var back=new List<SavedRoutePoint>{P(3,4),P(3,3),P(0,3),P(0,0)};
                var saved=OutdoorRoutineMigration.Rebuild(source,new List<List<SavedRoutePoint>>{personal,back},1.25,2,true);
                Check.Equal(2,saved.RouteGeometryVersion);Check.Equal(OutdoorRoutineMigration.CurrentPersonalLaneVersion,saved.PersonalLaneVersion);
                Check.Near(11.2,saved.Itinerary.Stops[0].WalkSeconds);Check.Near(11.2,saved.Itinerary.ExitWalkSeconds);
                int callbacks=0;
                var actual=OutdoorRoutineMigration.RuntimeItinerary(saved,line=>{callbacks++;return line;});
                var legacy=OutdoorRoutineMigration.RuntimeRoute(saved,line=>{callbacks++;return line;});
                Check.Equal(0,callbacks);Check.Equal(4,actual.Stops[0].Route.Count);Check.Equal(4,legacy.Count);
                Check.Near(11.2,actual.Stops[0].WalkSeconds);Check.Near(11.2,actual.ExitWalkSeconds);
            }));
            cases.Add(("Legacy and centerline saved routes upgrade once to exact personal geometry",()=>{
                foreach(int version in new[]{0,1})
                {
                    var route=new List<SavedRoutePoint>{P(0,0),P(3,4)};
                    var source=new ResidentRoutine {ResidentId="person",DestinationKey=2,OpenMinute=0,CloseMinute=1440,
                        Route=route,RouteGeometryVersion=version,ItineraryDepartureSeconds=30000,
                        Itinerary=new ResidentDestination {Stops=new List<ResidentVisitStop>{new ResidentVisitStop {
                            BuildingKey=2,Purpose=ResidentVisitPurpose.Social,WalkSeconds=8,DwellSeconds=600,Route=route}},
                            ExitRoute=new List<SavedRoutePoint>{P(3,4),P(0,0)},ExitWalkSeconds=8}};
                    int callbacks=0;
                    var prepared=OutdoorRoutineMigration.RuntimeItinerary(source,line=>{
                        callbacks++;return line[0].X==0 ? new List<SavedRoutePoint>{P(0,0),P(0,3),P(3,3),P(3,4)} :
                            new List<SavedRoutePoint>{P(3,4),P(3,3),P(0,3),P(0,0)};});
                    Check.Equal(2,callbacks);
                    var saved=OutdoorRoutineMigration.Rebuild(source,new List<List<SavedRoutePoint>>{
                        prepared.Stops[0].Route,prepared.ExitRoute},1.25,2,true);
                    Check.Equal(2,saved.RouteGeometryVersion);Check.Equal(OutdoorRoutineMigration.CurrentPersonalLaneVersion,saved.PersonalLaneVersion);
                    Check.Near(11.2,saved.Itinerary.Stops[0].WalkSeconds);
                    OutdoorRoutineMigration.RuntimeItinerary(saved,line=>{callbacks++;return line;});
                    Check.Equal(2,callbacks);Check.Equal(version,source.RouteGeometryVersion);Check.Equal(0,source.PersonalLaneVersion);
                    Check.Near(30000,saved.ItineraryDepartureSeconds);Check.Near(600,saved.Itinerary.Stops[0].DwellSeconds);
                }
            }));
            cases.Add(("Outdoor preparation batches cheap leaf steps within elapsed frame budget",()=>{
                int steps=0;double elapsed=0;
                System.Collections.IEnumerator Work()
                {for(int i=0;i<100;i++) {steps++;elapsed+=1.5;yield return null;}}
                var work=Work();
                Check.True(!BoundedWorkSlice.Advance(work,()=>elapsed,()=>true));
                Check.Equal(3,steps);
            }));
            cases.Add(("Outdoor preparation caps cheap work and stops paused slices before advancing",()=>{
                int steps=0;
                System.Collections.IEnumerator Work()
                {for(int i=0;i<100;i++) {steps++;yield return null;}}
                var work=Work();
                Check.True(!BoundedWorkSlice.Advance(work,()=>0,()=>true,8));Check.Equal(8,steps);
                Check.True(!BoundedWorkSlice.Advance(work,()=>0,()=>false,8));Check.Equal(8,steps);
            }));
            cases.Add(("Cached daily geometry migration replaces every leg at personal physical speed without mutating source",()=>{
                var original=new List<SavedRoutePoint>{P(0,0),P(3,0),P(3,4)};
                var back=new List<SavedRoutePoint>{P(3,4),P(0,4),P(0,0)};
                var source=new ResidentRoutine {ResidentId="person",DestinationKey=2,OpenMinute=0,CloseMinute=1440,
                    Route=original,ItineraryDepartureSeconds=30000,Itinerary=new ResidentDestination {
                        Stops=new List<ResidentVisitStop>{new ResidentVisitStop {BuildingKey=2,Purpose=ResidentVisitPurpose.Social,
                            WalkSeconds=14,DwellSeconds=600,Route=original}},ExitRoute=back,ExitWalkSeconds=14}};
                var direct=new List<SavedRoutePoint>{P(0,0),P(3,4)};
                var reverse=new List<SavedRoutePoint>{P(3,4),P(0,0)};
                var migrated=OutdoorRoutineMigration.Rebuild(source,new List<List<SavedRoutePoint>>{direct,reverse},1.25,2);
                Check.Equal(2,migrated.Itinerary.Stops[0].Route.Count);
                Check.Near(8,migrated.Itinerary.Stops[0].WalkSeconds);Check.Near(8,migrated.Itinerary.ExitWalkSeconds);
                Check.Near(30000,migrated.ItineraryDepartureSeconds);Check.Near(600,migrated.Itinerary.Stops[0].DwellSeconds);
                Check.Equal(OutdoorRoutineMigration.CenterlineVersion,migrated.RouteGeometryVersion);Check.Equal(2,migrated.DestinationKey);
                Check.True(OutdoorRoutineMigration.NeedsCenterlineRebuild(new ResidentRoutine {RouteGeometryVersion=1}));
                migrated.Itinerary.Stops[0].Route[1].X=999;
                Check.Near(3,source.Itinerary.Stops[0].Route[1].X);Check.Near(3,direct[1].X);
                Check.Near(14,source.Itinerary.Stops[0].WalkSeconds);Check.Equal(0,source.RouteGeometryVersion);
            }));
            cases.Add(("Cached geometry publication never replaces active travel visit or changed departure boundary",()=>{
                var home=new ActivitySnapshot(Activity.Home,1,new RoutePoint(),0);
                var travel=new ActivitySnapshot(Activity.Travel,-1,new RoutePoint(4,5),0.5);
                var visit=new ActivitySnapshot(Activity.Visit,2,new RoutePoint(3,4),0);
                Check.True(OutdoorRoutineMigration.CanPublishAtHome(home,home,1));
                Check.True(!OutdoorRoutineMigration.CanPublishAtHome(travel,home,1));
                Check.True(!OutdoorRoutineMigration.CanPublishAtHome(home,travel,1));
                Check.True(!OutdoorRoutineMigration.CanPublishAtHome(visit,home,1));
            }));
            cases.Add(("Ordinary resident without regional trip can publish prepared geometry at home",()=>{
                var store=new WorldStateStore(42);var person=store.GetOrCreate(100,new[]{10}).Households[0].Residents[0];
                var home=new ActivitySnapshot(Activity.Home,person.HomeKey,new RoutePoint(),0);
                Check.True(!store.IsResidentOnHomeRoute(person.Id,1000));
                Check.True(OutdoorRoutineMigration.CanPublishAtHome(store,person.Id,1000,home,home,person.HomeKey));
            }));
            cases.Add(("Sliced corridor lane preparation bounds every native width and sweep callback",()=>{
                var route=new[] {new RoutePoint(0,0),new RoutePoint(24,0)};int callbacks=0;
                var operation=CorridorLaneRoutes.Begin(route,27,(p,n)=>{callbacks++;return new LaneCrossSection(4,4);},
                    (a,b)=>{callbacks++;return Math.Abs(a.Z)<0.2 && Math.Abs(b.Z)<0.2;});
                int steps=0;while(!operation.IsComplete)
                {int before=callbacks;operation.Step(2);Check.True(callbacks-before<=2);Check.True(++steps<300);}
                Check.True(operation.Points.Count>=2);
                foreach(var point in operation.Points) Check.True(Math.Abs(point.Z)<0.2);
            }));
            cases.Add(("Cached geometry rejects disconnected replacement without touching saved itinerary",()=>{
                var route=new List<SavedRoutePoint>{P(0,0),P(3,4)};
                var source=new ResidentRoutine {ResidentId="person",DestinationKey=2,OpenMinute=0,CloseMinute=1440,Route=route};
                Check.Throws<ArgumentException>(()=>OutdoorRoutineMigration.Rebuild(source,
                    new List<List<SavedRoutePoint>>{new List<SavedRoutePoint>{P(1,0),P(3,4)}},1.25,2));
                Check.Equal(0,source.RouteGeometryVersion);Check.Near(0,source.Route[0].X);
            }));
            cases.Add(("Four-point outdoor routes bend smoothly after exact door connector",()=>{
                var curve=OutdoorRouteSmoother.RoundCorner(P(-2,0),P(0,0),P(0,8),(a,b)=>true,true,false);
                Check.True(curve.Count>=5,"Protected doorway corner has no soft turn.");
                Check.Near(0,curve[0].X);Check.Near(0,curve[0].Z);
                Check.True(curve[1].X>0 && curve[1].Z>0,"Curve does not continue doorway tangent.");
                Check.Near(0,curve[curve.Count-1].X);Check.True(curve[curve.Count-1].Z>0);
            }));
            cases.Add(("Obstacle turns try supported outside arc before keeping sharp corner",()=>{
                Func<SavedRoutePoint,SavedRoutePoint,bool> clear=(a,b)=>{
                    for(int i=0;i<=50;i++) {double t=i/50d;
                        double x=a.X+(b.X-a.X)*t,z=a.Z+(b.Z-a.Z)*t;
                        if(x<5.99 && z>0.01) return false;
                    }
                    return true;
                };
                var curve=OutdoorRouteSmoother.RoundCorner(P(0,0),P(6,0),P(6,6),clear);
                Check.True(curve.Count>=5,"Clear outside arc was not attempted around blocked inside turn.");
                var previous=P(0,0);foreach(var point in curve) {Check.True(clear(previous,point));previous=point;}
                Check.True(clear(previous,P(6,6)));
            }));
            cases.Add(("Sliced obstacle rounding bounds native callback work",()=>{
                int checks=0;
                var operation=OutdoorRouteSmoother.BeginCorner(P(0,0),P(6,0),P(6,6),(a,b)=>{checks++;return false;});
                while(!operation.IsComplete)
                {int before=checks;operation.Step(2);Check.True(checks-before<=2);}
                Check.True(checks<=50);Check.Equal(1,operation.Points.Count);
            }));
            cases.Add(("Final outdoor street curve preserves exact outgoing doorway control",()=>{
                var curve=OutdoorRouteSmoother.RoundCorner(P(0,-8),P(0,0),P(2,0),(a,b)=>true,false,true);
                Check.True(curve.Count>=5);
                var last=curve[curve.Count-1];Check.Near(0,last.X);Check.Near(0,last.Z);
                Check.True(curve[curve.Count-2].X<0 && curve[curve.Count-2].Z<0);
                var rejected=OutdoorRouteSmoother.RoundCorner(P(0,-8),P(0,0),P(2,0),(a,b)=>false,false,true);
                Check.Equal(1,rejected.Count);Check.Near(0,rejected[0].X);Check.Near(0,rejected[0].Z);
            }));
            cases.Add(("Outdoor doorway connector need not belong to street nav footprint",()=>{
                var curve=OutdoorRouteSmoother.RoundCorner(P(-2,0),P(0,0),P(0,8),
                    (a,b)=>a.X>=-0.00001 && b.X>=-0.00001,true,false);
                Check.True(curve.Count>1,"Already validated exact doorway segment blocked street-side curve.");
                Check.Near(0,curve[0].X);Check.Near(0,curve[0].Z);
            }));
            cases.Add(("Same-cell doorway routes retain sole exact approach control",()=>{
                var line=new[] {new RoutePoint(0,0),new RoutePoint(1,1),new RoutePoint(2,0)};
                int surveys=0;
                var result=CorridorLaneRoutes.OffsetWithApproaches(line,27,(p,n)=>{surveys++;return new LaneCrossSection(4,4);},(a,b)=>true);
                Check.Equal(3,result.Count);Check.Near(1,result[1].X);Check.Near(1,result[1].Z);Check.Equal(0,surveys);
            }));
            cases.Add(("Synchronous outdoor smoothing honors small caller budget",()=>{
                var route=new List<SavedRoutePoint>();for(int i=0;i<100;i++) route.Add(P(i,0));
                int checks=0;
                var operation=OutdoorRouteSmoother.Begin(route,(a,b)=>{checks++;return false;},8);
                while(!operation.IsComplete) operation.Step(2);
                Check.Equal(8,checks);Check.Equal(100,operation.Points.Count);
            }));
            cases.Add(("Ordinary posts admit broad straight streets and reject narrow edge strips",()=>{
                Func<int,int,int> broad=(x,z)=>Math.Abs(z)<=2 && Math.Abs(x)<=8 ? 15 : 0;
                Func<int,int,int> narrow=(x,z)=>z==0 && Math.Abs(x)<=8 ? 15 : 0;
                Check.True(GuardPostSites.IsBroadStreet(0,0,broad,4,2));
                Check.True(!GuardPostSites.IsBroadStreet(0,0,narrow,4,2));
                Check.True(!GuardPostSites.IsJunction(0,0,broad,8));
            }));
            cases.Add(("Outdoor smoothing removes clear grid stairs and retains exact door connectors",()=>{
                var route=new List<SavedRoutePoint> { P(-1,0),P(0,0),P(1,0),P(1,1),P(2,1),P(2,2),P(3,2),P(3,3),P(4,3) };
                var result=OutdoorRouteSmoother.Smooth(route,(a,b)=>true);
                Check.Equal(4,result.Count);
                Check.Near(-1,result[0].X);Check.Near(0,result[1].X);
                Check.Near(3,result[2].X);Check.Near(3,result[2].Z);Check.Near(4,result[3].X);
            }));
            cases.Add(("Outdoor smoothing retains obstructed corners",()=>{
                var route=new List<SavedRoutePoint> {P(-1,0),P(0,0),P(4,0),P(4,4),P(5,4)};
                var result=OutdoorRouteSmoother.Smooth(route,(a,b)=>false);
                Check.Equal(5,result.Count);Check.Near(4,result[2].X);Check.Near(0,result[2].Z);
            }));
            cases.Add(("Outdoor corner curves use swept soft turns and reject obstructed arcs",()=>{
                var curve=OutdoorRouteSmoother.RoundCorner(P(0,0),P(6,0),P(6,6),(a,b)=>true);
                Check.True(curve.Count>=5);
                Check.True(curve[0].X<6 && curve[0].Z==0);
                Check.True(curve[curve.Count-1].X==6 && curve[curve.Count-1].Z>0);
                for(int i=1;i<curve.Count-1;i++) Check.True(curve[i].X<6 && curve[i].Z>0);
                var rejected=OutdoorRouteSmoother.RoundCorner(P(0,0),P(6,0),P(6,6),(a,b)=>false);
                Check.Equal(1,rejected.Count);Check.Near(6,rejected[0].X);Check.Near(0,rejected[0].Z);
            }));
            cases.Add(("Sliced outdoor smoothing bounds checks and leaves original input unchanged",()=>{
                var route=new List<SavedRoutePoint>();
                for(int i=0;i<160;i++) route.Add(P(i/2,i%2));
                int callbacks=0;
                var operation=OutdoorRouteSmoother.Begin(route,(a,b)=>{callbacks++;return false;});
                int calls=0;
                while(!operation.IsComplete)
                {
                    int before=callbacks;operation.Step(2);Check.True(callbacks-before<=2);
                    Check.True(++calls<2100);
                }
                Check.True(callbacks<=4096);Check.Equal(160,operation.Points.Count);
                operation.Points[1].X=500;Check.Near(0,route[1].X);
            }));
            cases.Add(("Corridor lanes spread across asymmetric verified road width",()=>{
                var line=new[] {new RoutePoint(0,0),new RoutePoint(20,0)};
                double min=100,max=-100;
                for(uint seed=1;seed<=60;seed++)
                {
                    var result=CorridorLaneRoutes.Offset(line,seed,(p,n)=>new LaneCrossSection(5,1),(a,b)=>true);
                    Check.True(result.Count>=4);
                    Check.Near(0,result[0].Z);Check.Near(0,result[result.Count-1].Z);
                    double z=result[result.Count/2].Z;
                    Check.True(z>=-1 && z<=5);min=Math.Min(min,z);max=Math.Max(max,z);
                    var repeat=CorridorLaneRoutes.Offset(line,seed,(p,n)=>new LaneCrossSection(5,1),(a,b)=>true);
                    Check.Near(z,repeat[repeat.Count/2].Z);
                }
                Check.True(min<0 && max>4,"Walkers did not use broad supported road.");
            }));
            cases.Add(("Road edge centerlines use a fully paved interval displaced into the road",()=>{
                var line=new[] {new RoutePoint(0,0),new RoutePoint(20,0)};
                double minimum=100,maximum=-100;
                for(uint seed=1;seed<=32;seed++)
                {
                    var lane=CorridorLaneRoutes.Offset(line,seed,(p,n)=>new LaneCrossSection(5,0,0.5),(a,b)=>true);
                    double z=lane[lane.Count/2].Z;
                    Check.True(z>=0.5 && z<=5.5);minimum=Math.Min(minimum,z);maximum=Math.Max(maximum,z);
                }
                Check.True(minimum<1.5 && maximum>4.5);
            }));
            cases.Add(("Narrow corridor section does not collapse broad sections of same lane",()=>{
                var route=CorridorLaneRoutes.Offset(new[] {new RoutePoint(0,0),new RoutePoint(20,0)},27,
                    (p,n)=>p.X>=8 && p.X<=12 ? new LaneCrossSection(0,0) : new LaneCrossSection(4,4),(a,b)=>true);
                bool broad=false,narrow=false;
                foreach(var p in route)
                {
                    if(p.X>=8 && p.X<=12) {Check.Near(0,p.Z);narrow=true;}
                    if(p.X>1 && p.X<7 && Math.Abs(p.Z)>0.5) broad=true;
                }
                Check.True(broad && narrow);
            }));
            cases.Add(("Corridor lanes keep supported controls when one local span is blocked",()=>{
                var route=CorridorLaneRoutes.Offset(new[] {new RoutePoint(0,0),new RoutePoint(24,0)},27,
                    (p,n)=>new LaneCrossSection(4,4),
                    (a,b)=>(a.X>=8 && a.X<=16 ? Math.Abs(a.Z)<0.2 : true) &&
                        (b.X>=8 && b.X<=16 ? Math.Abs(b.Z)<0.2 : true));
                bool wide=false;
                foreach(var p in route)
                {
                    if(p.X>=8 && p.X<=16) Check.True(Math.Abs(p.Z)<0.2);
                    if(p.X==4 || p.X==20) wide|=Math.Abs(p.Z)>0.5;
                }
                Check.True(wide);
            }));
            cases.Add(("Outdoor shortcuts never call native sweep beyond bounded span",()=>{
                var route=new List<SavedRoutePoint>();
                for(int i=0;i<200;i++) route.Add(P(i,0));
                var result=OutdoorRouteSmoother.Smooth(route,(a,b)=>{
                    Check.True(Math.Abs(a.X-b.X)<=24);return true;
                });
                Check.True(result.Count<20);Check.Near(199,result[result.Count-1].X);
            }));
            cases.Add(("Corridor lane work stays bounded for oversized saved routes",()=>{
                var route=new List<RoutePoint>();for(int i=0;i<3000;i++) route.Add(new RoutePoint(i,0));
                int surveys=0,sweeps=0;
                var result=CorridorLaneRoutes.Offset(route,27,(p,n)=>{surveys++;return new LaneCrossSection(4,4);},
                    (a,b)=>{sweeps++;return true;});
                Check.Equal(0,surveys);Check.Equal(0,sweeps);Check.Equal(3000,result.Count);
            }));
        }
        private static SavedRoutePoint P(double x,double z) {return new SavedRoutePoint {X=x,Z=z};}
    }
}
