using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class HauntingTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Renewed threat clears expired native response links before reacquiring witnesses",()=>{
                var store=new WorldStateStore(42); store.GetOrCreate(99,new[] { 1 });
                var native=store.RegisterNativePerson(99,1,123,"Sample Name",ResidentSex.Male,0,0,182,0);
                var alarm=store.RecordHostileThreat(99,"native:123",100);
                store.Witness(alarm.Id,native.Id,101);
                store.RecordHostileThreat(99,"native:123",1001);
                Check.True(store.NativeResponse(native.Id)==null);
                Check.Equal(1,store.Snapshot().AlarmIncidents.Count);
            }));
            cases.Add(("Hostile threat saves reject fighting or a fabricated murder victim",()=>{
                var store=new WorldStateStore(42); var town=store.GetOrCreate(99,new[] { 1 });
                var alarm=store.RecordHostileThreat(99,"native:123",100); string id=town.Households[0].Residents[0].Id;
                store.Witness(alarm.Id,id,101);
                var bad=store.Snapshot(); bad.AlarmIncidents[0].People[0].Reaction=CivilianReaction.Fight;
                Check.Throws<ArgumentException>(()=>store.Restore(bad));
                bad=store.Snapshot(); bad.AlarmIncidents[0].VictimId=id;
                Check.Throws<ArgumentException>(()=>store.Restore(bad));
            }));
            cases.Add(("Hostile attacker alarms make citizens flee without a murder victim and survive reload",()=>{
                var store=new WorldStateStore(42); var town=store.GetOrCreate(99,new[] { 1,2,3 });
                string first=town.Households[0].Residents[0].Id,second=town.Households[1].Residents[0].Id,third=town.Households[2].Residents[0].Id;
                var alarm=store.RecordHostileThreat(99,"native:123",100);
                Check.True(alarm.VictimId==null && alarm.Corpse==null && alarm.Scene==null);
                Check.Equal(CivilianReaction.Flee,store.Witness(alarm.Id,first,101).Reaction);
                Check.Equal(true,store.Warn(alarm.Id,first,second,102));
                Check.Equal(false,store.Warn(alarm.Id,second,third,103));
                var restored=new WorldStateStore(42); restored.Restore(store.Snapshot());
                Check.Equal("native:123",restored.ActiveAlarm(second,104).ThreatId);
                Check.Equal(CivilianReaction.Flee,restored.ActiveAlarm(second,104).People.Find(p=>p.ResidentId==second).Reaction);
            }));
            cases.Add(("Crowd sweeps cannot jump through a walker in a narrow corridor",()=>{
                var result=CrowdSteering.Move(new GuardPoint3 { X=-2 },new GuardPoint3 { X=2 },
                    new[] { new GuardPoint3() },0.82,4,p=>Math.Abs(p.Z)<0.00001);
                Check.Equal(-2d,result.X);
            }));
            cases.Add(("Crowd bodies on separate floors do not obstruct one another",()=>{
                var result=CrowdSteering.Move(new GuardPoint3(),new GuardPoint3 { X=1 },
                    new[] { new GuardPoint3 { Y=3 } },0.82,0.1,p=>true);
                Check.Equal(0.1,result.X);
            }));
            cases.Add(("Crowd steering passes opposite walkers without crossing occupied space",()=>{
                var a=new GuardPoint3 { X=-2 }; var b=new GuardPoint3 { X=2 };
                double minimum=4;
                for(int step=0;step<100;step++)
                {
                    a=CrowdSteering.Move(a,new GuardPoint3 { X=2 },new[] { b },0.6,0.1,p=>true);
                    b=CrowdSteering.Move(b,new GuardPoint3 { X=-2 },new[] { a },0.6,0.1,p=>true);
                    double dx=a.X-b.X,dz=a.Z-b.Z;
                    minimum=Math.Min(minimum,Math.Sqrt(dx*dx+dz*dz));
                }
                Check.True(minimum>=0.6-0.00001);
                Check.True(a.X>1.8 && b.X< -1.8);
            }));
            cases.Add(("Crowd steering waits when every step is blocked",()=>{
                var position=CrowdSteering.Move(new GuardPoint3(),new GuardPoint3 { X=1 },new GuardPoint3[0],0.6,0.1,p=>false);
                Check.Equal(0d,position.X); Check.Equal(0d,position.Z);
            }));
            cases.Add(("Crowd steering recovers initial overlap only while separation increases",()=>{
                var from=new GuardPoint3();
                var neighbour=new GuardPoint3 { X=0.51 };
                var away=CrowdSteering.Move(from,new GuardPoint3 { X=-1 },new[] { neighbour },0.52,0.1,p=>true);
                Check.True(away.X<from.X);
                Check.True(Math.Abs(away.X-neighbour.X)>Math.Abs(from.X-neighbour.X));
                var toward=CrowdSteering.Move(from,new GuardPoint3 { X=1 },new[] { neighbour },0.52,0.1,
                    p=>Math.Abs(p.Z)<0.000001);
                Check.Equal(0d,toward.X);
                Check.Equal(0d,toward.Z);
            }));
            cases.Add(("Crowd steering overlap recovery cannot cross another neighbor",()=>{
                var position=CrowdSteering.Move(new GuardPoint3(),new GuardPoint3 { X=-1 },
                    new[] { new GuardPoint3 { X=0.4 },new GuardPoint3 { X=-0.6 } },0.52,1,
                    p=>Math.Abs(p.Z)<0.000001);
                Check.Equal(0d,position.X);
                Check.Equal(0d,position.Z);
            }));
            cases.Add(("Crowd comfort steering deflects before hard separation is threatened",()=>{
                int side=0;
                var position=CrowdSteering.MoveComfort(new GuardPoint3(),new GuardPoint3 { X=10 },
                    new[] { new GuardPoint3 { X=1.4 } },0.52,1.1,1.8,0.1,ref side,p=>true);
                Check.True(position.X>0);
                Check.True(position.Z>0);
                Check.Equal(1,side);
            }));
            cases.Add(("Crowd comfort steering falls back to hard separation beside a wall",()=>{
                int side=0;
                var position=CrowdSteering.MoveComfort(new GuardPoint3(),new GuardPoint3 { X=10 },
                    new[] { new GuardPoint3 { X=1.4 } },0.52,1.1,1.8,0.1,ref side,p=>Math.Abs(p.Z)<0.000001);
                Check.Near(0.1,position.X);
                Check.Near(0,position.Z);
            }));
            cases.Add(("Crowd comfort steering ignores neighbors behind and on other floors",()=>{
                int side=0;
                var behind=CrowdSteering.MoveComfort(new GuardPoint3(),new GuardPoint3 { X=1 },
                    new[] { new GuardPoint3 { X=-0.8 } },0.52,1.1,1.8,0.1,ref side,p=>true);
                Check.Near(0.1,behind.X); Check.Near(0,behind.Z);
                var otherFloor=CrowdSteering.MoveComfort(new GuardPoint3(),new GuardPoint3 { X=1 },
                    new[] { new GuardPoint3 { X=0.8,Y=3 } },0.52,1.1,1.8,0.1,ref side,p=>true);
                Check.Near(0.1,otherFloor.X); Check.Near(0,otherFloor.Z);
            }));
            cases.Add(("Crowd comfort steering retains its passing side while the conflict shifts",()=>{
                int side=0;
                var first=CrowdSteering.MoveComfort(new GuardPoint3(),new GuardPoint3 { X=10 },
                    new[] { new GuardPoint3 { X=1.4,Z=-0.1 } },0.52,1.1,1.8,0.1,ref side,p=>true);
                var second=CrowdSteering.MoveComfort(new GuardPoint3 { X=0.1,Z=first.Z },new GuardPoint3 { X=10 },
                    new[] { new GuardPoint3 { X=1.3,Z=0.1 } },0.52,1.1,1.8,0.1,ref side,p=>true);
                Check.True(first.Z>0); Check.True(second.Z>0); Check.Equal(1,side);
            }));
            cases.Add(("Crowd comfort steering keeps sloped 3D steps within the frame limit",()=>{
                int side=0;
                var position=CrowdSteering.MoveComfort(new GuardPoint3(),new GuardPoint3 { X=10,Y=10 },
                    new[] { new GuardPoint3 { X=1.4,Y=1.4 } },0.52,1.1,1.8,0.1,ref side,p=>true);
                double moved=Math.Sqrt(position.X*position.X+position.Y*position.Y+position.Z*position.Z);
                Check.True(moved<=0.1+0.000001);
            }));
            cases.Add(("Crowd comfort steering ignores neighbors beyond the intended endpoint",()=>{
                int side=0;
                var position=CrowdSteering.MoveComfort(new GuardPoint3(),new GuardPoint3 { X=1 },
                    new[] { new GuardPoint3 { X=2 } },0.52,1.1,2.5,0.1,ref side,p=>true);
                Check.Near(0.1,position.X); Check.Near(0,position.Z); Check.Equal(0,side);
            }));
            cases.Add(("Crowd comfort steering anticipates beyond a short continuing waypoint",()=>{
                int side=0;
                var position=CrowdSteering.MoveComfort(new GuardPoint3(),new GuardPoint3 { X=0.1 },
                    new[] { new GuardPoint3 { X=1.5 } },0.52,1.1,1.8,0.1,ref side,p=>true,true);
                double moved=Math.Sqrt(position.X*position.X+position.Y*position.Y+position.Z*position.Z);
                Check.True(position.Z>0);
                Check.True(moved<=0.1+0.000001);
            }));
            cases.Add(("Interpolated crowd targets preserve the walking comfort distance",()=>{
                var a=new GuardPoint3 { X=-3 };
                var b=new GuardPoint3 { X=3 };
                int sideA=0,sideB=0;
                double offsetA=0,offsetB=0;
                double minimum=6;
                string closest="";
                const double dt=0.1,scheduledSpeed=1.3;
                for(int frame=1;frame<=60;frame++)
                {
                    double elapsed=frame*dt,step=scheduledSpeed*dt;
                    var targetA=new GuardPoint3 { X=-3+scheduledSpeed*elapsed };
                    var targetB=new GuardPoint3 { X=3-scheduledSpeed*elapsed };
                    a=CrowdSteering.MoveComfort(a,targetA,new[] { b },0.52,1.1,1.8,step,ref sideA,ref offsetA,
                        new GuardPoint3 { X=1 },p=>true,true);
                    b=CrowdSteering.MoveComfort(b,targetB,new[] { a },0.52,1.1,1.8,step,ref sideB,ref offsetB,
                        new GuardPoint3 { X=-1 },p=>true,true);
                    double dx=a.X-b.X,dz=a.Z-b.Z;
                    double distance=Math.Sqrt(dx*dx+dz*dz);
                    if(distance<minimum) { minimum=distance; closest=frame+" A="+a.X+","+a.Z+" B="+b.X+","+b.Z+" sides="+sideA+","+sideB; }
                }
                Check.True(minimum>=1.0,"Minimum center distance was "+minimum+" at "+closest);
            }));
            cases.Add(("Visit windows retain exact arrival and departure across calendar days",()=>{
                var schedule=DailySchedule.DayVisit(1,2,new[] { new RoutePoint(0,0),new RoutePoint(10,0) },540,1080,30,1,12,0,360,1320);
                double start,end;
                Check.Equal(false,schedule.TryVisitWindow(539*60,out start,out end));
                Check.Equal(true,schedule.TryVisitWindow(86400+540*60+0.5,out start,out end));
                Check.Equal(86400d+540*60,start); Check.Equal(86400d+570*60,end);
                Check.Equal(false,schedule.TryVisitWindow(end,out start,out end));
            }));
            cases.Add(("Shop journeys reject disconnected routes and insufficient activity time",()=>{
                var paths=new List<List<GuardPoint3>>();
                for(int i=0;i<4;i++) paths.Add(new List<GuardPoint3> { new GuardPoint3(),new GuardPoint3() });
                Check.Throws<ArgumentException>(()=>ShopVisitJourney.Create(0,59,paths,1,12));
                paths[1][0].X=1;
                Check.Throws<ArgumentException>(()=>ShopVisitJourney.Create(0,600,paths,1,12));
            }));
            cases.Add(("Shop journey reconstructs browsing haggling and departure without frame history",()=>{
                var routes=new List<List<GuardPoint3>> {
                    new List<GuardPoint3> { new GuardPoint3(),new GuardPoint3 { X=10 } },
                    new List<GuardPoint3> { new GuardPoint3 { X=10 },new GuardPoint3 { X=20 } },
                    new List<GuardPoint3> { new GuardPoint3 { X=20 },new GuardPoint3 { X=30 } },
                    new List<GuardPoint3> { new GuardPoint3 { X=30 },new GuardPoint3() } };
                var journey=ShopVisitJourney.Create(100,460,routes,1,1);
                var patron=ShopVisitJourney.Create(100,460,routes,1,1,true,0,true);
                Check.Equal(ShopVisitPhase.Haggling,patron.Evaluate(111).Phase);
                Check.Equal(ShopVisitPhase.Browsing,patron.Evaluate(140).Phase);
                Check.Equal(ShopVisitPhase.Leaving,patron.Evaluate(445).Phase);
                var occupancy=new List<ShopOccupancy>(journey.Occupancies());
                Check.Equal(3,occupancy.Count);Check.Near(110,occupancy[0].Start);
                foreach(var occupied in occupancy)
                {
                    var sample=journey.Evaluate((occupied.Start+occupied.End)/2);
                    Check.True(sample.Phase==ShopVisitPhase.Browsing || sample.Phase==ShopVisitPhase.Haggling);
                    Check.Near(occupied.Position.X,sample.Position.X);
                }
                Check.Equal(ShopVisitPhase.Walking,journey.Evaluate(105).Phase);
                Check.Equal(5d,journey.Evaluate(105).Position.X);
                Check.Equal(ShopVisitPhase.Browsing,journey.Evaluate(115).Phase);
                Check.Equal(ShopVisitPhase.Haggling,journey.Evaluate(428).Phase);
                Check.Equal(ShopVisitPhase.Leaving,journey.Evaluate(445).Phase);
                Check.Equal(15d,journey.Evaluate(445).Position.X);
                Check.Equal(ShopVisitPhase.Complete,journey.Evaluate(460).Phase);
                Check.Equal(journey.Evaluate(445).Position.X,ShopVisitJourney.Create(100,460,routes,1,1).Evaluate(445).Position.X);
                routes[0][1].X=999;
                Check.Equal(10d,journey.Evaluate(115).Position.X);
            }));
            cases.Add(("Shop visits allow several browsing stops and a short counter interaction",()=>{
                var routes=new List<List<GuardPoint3>>();
                for(int i=0;i<5;i++) routes.Add(new List<GuardPoint3> { new GuardPoint3 { X=i },new GuardPoint3 { X=i+1 } });
                routes.Add(new List<GuardPoint3> { new GuardPoint3 { X=5 },new GuardPoint3() });
                var journey=ShopVisitJourney.Create(0,1200,routes,1,12);
                var stops=new HashSet<double>(); int counterSeconds=0;
                for(int second=0;second<1200;second++)
                {
                    var sample=journey.Evaluate(second);
                    if(sample.Phase==ShopVisitPhase.Browsing) stops.Add(sample.Position.X);
                    if(sample.Phase==ShopVisitPhase.Haggling) counterSeconds++;
                }
                Check.Equal(4,stops.Count);
                Check.True(counterSeconds>=36 && counterSeconds<=84);
                Check.Equal(ShopVisitPhase.Complete,journey.Evaluate(1200).Phase);
            }));
            cases.Add(("Nonbuyers browse then leave without a counter stop and reconstruct identically",()=>{
                var routes=new List<List<GuardPoint3>> {
                    new List<GuardPoint3> { new GuardPoint3(),new GuardPoint3 { X=4 } },
                    new List<GuardPoint3> { new GuardPoint3 { X=4 },new GuardPoint3 { X=8 } },
                    new List<GuardPoint3> { new GuardPoint3 { X=8 },new GuardPoint3() } };
                var first=ShopVisitJourney.Create(100,1300,routes,1.3,12,false,73);
                var restored=ShopVisitJourney.Create(100,1300,routes,1.3,12,false,73);
                Check.True(!first.Purchases); bool browsing=false,leaving=false;
                for(int second=100;second<=1300;second++)
                {
                    var a=first.Evaluate(second); var b=restored.Evaluate(second);
                    Check.True(a.Phase!=ShopVisitPhase.Haggling);
                    Check.Equal(a.Phase,b.Phase); Check.Near(a.Position.X,b.Position.X);
                    browsing|=a.Phase==ShopVisitPhase.Browsing; leaving|=a.Phase==ShopVisitPhase.Leaving;
                }
                Check.True(browsing && leaving);
            }));
            cases.Add(("Settlement coverage admits native cities towns and villages only",()=>{
                for(int category=-1;category<=15;category++)
                    Check.Equal(category>=0 && category<=2,SettlementClassification.SupportsPopulation(category));
                Check.Equal(false,SettlementClassification.SupportsPopulation(65535));
            }));
            cases.Add(("Single-name cultures get a stable shared family name",()=>{
                var home=new Household { SurnameSeed=42 };
                home.Residents.Add(new Resident { AppearanceSeed=1 });
                home.Residents.Add(new Resident { AppearanceSeed=2 });
                RegionalIdentity.Initialize(home,2,(seed,sex)=>"Name"+seed,seed=>string.Empty);
                Check.Equal("Name42",home.Surname);
                Check.Equal("Name1 Name42",home.Residents[0].FullName);
                Check.Equal("Name2 Name42",home.Residents[1].FullName);
                RegionalIdentity.Initialize(home,2,(seed,sex)=>"Changed",seed=>string.Empty);
                Check.Equal("Name1 Name42",home.Residents[0].FullName);
            }));
            cases.Add(("Regional identity persists shared surname and preserves existing names and race",()=>{
                var home=new Household { SurnameSeed=42 };
                home.Residents.Add(new Resident { Id="a",AppearanceSeed=1 });
                home.Residents.Add(new Resident { Id="b",AppearanceSeed=2,FullName="Existing Name",Race=4 });
                RegionalIdentity.Initialize(home,2,(seed,sex)=>"Given"+seed,seed=>"Family"+seed);
                Check.Equal("Given1 Family42",home.Residents[0].FullName);
                Check.Equal(2,home.Residents[0].Race); Check.Equal(4,home.Residents[1].Race);
                Check.Equal("Existing Name",home.Residents[1].FullName);
                RegionalIdentity.Initialize(home,3,(seed,sex)=>"Changed",seed=>"Other");
                Check.Equal("Given1 Family42",home.Residents[0].FullName); Check.Equal(2,home.Residents[0].Race);
            }));
            cases.Add(("Settlement scale follows location category rather than named city IDs",()=>{
                Check.Equal(SettlementScale.City,SettlementClassification.FromLocationType(0));
                Check.Equal(SettlementScale.Town,SettlementClassification.FromLocationType(1));
                Check.Equal(SettlementScale.Hamlet,SettlementClassification.FromLocationType(2));
                Check.Equal(16,GuardRoster.TargetCount(SettlementClassification.FromLocationType(0),10));
                Check.Equal(8,GuardRoster.TargetCount(SettlementClassification.FromLocationType(1),10));
                Check.Equal(1,GuardRoster.TargetCount(SettlementClassification.FromLocationType(2),10));
                foreach(int other in new[] { -1,3,4,5,6,7,8,9,10,11,12,13,14,65535 })
                    Check.Throws<ArgumentOutOfRangeException>(()=>SettlementClassification.FromLocationType(other));
            }));
            cases.Add(("Legacy saves default daily haunting policy and invalid saved days reject transactionally",()=>{
                var store=new WorldStateStore(42); store.Restore(new WorldState { WorldSeed=42,HauntingDay=null });
                Check.True(store.RestrictHauntingRoutines(1291010263,false,600));
                var bad=store.Snapshot(); bad.HauntingDay.Day=0.5;
                Check.Throws<ArgumentException>(()=>store.Restore(bad));
                Check.True(store.RestrictHauntingRoutines(1291010263,true,1200));
            }));
            cases.Add(("Civilian haunting policy survives saves and changes only with the next day",()=>{
                var store=new WorldStateStore(42);
                Check.True(store.RestrictHauntingRoutines(1291010263,false,600));
                Check.True(store.RestrictHauntingRoutines(1291010263,true,1200));
                var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.True(restored.RestrictHauntingRoutines(1291010263,true,1800));
                Check.True(!restored.RestrictHauntingRoutines(1291010263,true,86400));
                Check.True(!restored.RestrictHauntingRoutines(206728581,false,86400));
                Check.Throws<ArgumentOutOfRangeException>(()=>restored.RestrictHauntingRoutines(1291010263,false,double.NaN));
            }));
            cases.Add(("Unloaded refuge release uses first dawn even when revisiting on a later haunted night",()=>{
                Check.True(!SettlementHaunting.FirstSafeTime(1291010263,false,20*3600,86400+5*3600-0.01).HasValue);
                Check.Near(86400+5*3600,SettlementHaunting.FirstSafeTime(1291010263,false,20*3600,86400+5*3600).Value);
                Check.Near(86400+5*3600,SettlementHaunting.FirstSafeTime(1291010263,false,20*3600,3*86400+22*3600).Value);
                Check.Near(5*3600,SettlementHaunting.FirstSafeTime(1291010263,false,2*3600,22*3600).Value);
            }));
            cases.Add(("Curse resolution release uses observation time unless an earlier dawn is known",()=>{
                Check.Near(21*3600,SettlementHaunting.FirstSafeTime(1291010263,true,20*3600,21*3600).Value);
                Check.Near(86400+5*3600,SettlementHaunting.FirstSafeTime(1291010263,true,20*3600,3*86400).Value);
                Check.Throws<ArgumentOutOfRangeException>(()=>SettlementHaunting.FirstSafeTime(1291010263,false,200,100));
            }));
            cases.Add(("Daggerfall haunting changes at exact 19:00 and 05:00 boundaries", () => {
                const int city = 1291010263;
                Check.True(!SettlementHaunting.StreetsDangerous(city, false, 19*3600-0.01));
                Check.True(SettlementHaunting.StreetsDangerous(city, false, 19*3600));
                Check.True(SettlementHaunting.StreetsDangerous(city, false, 86400+5*3600-0.01));
                Check.True(!SettlementHaunting.StreetsDangerous(city, false, 86400+5*3600));
                Check.True(SettlementHaunting.StreetsDangerous(city, false, 123456*86400d));
            }));
            cases.Add(("Curse resolution immediately restores Daggerfall streets without affecting other settlements", () => {
                Check.True(!SettlementHaunting.StreetsDangerous(1291010263, true, 23*3600));
                Check.True(!SettlementHaunting.StreetsDangerous(206728581, false, 23*3600));
                Check.True(SettlementHaunting.StreetsDangerous(1291010263, false, 23*3600));
            }));
            cases.Add(("Haunting rejects invalid game timestamps", () => {
                foreach (double value in new[] { -1, double.NaN, double.PositiveInfinity })
                    Check.Throws<ArgumentOutOfRangeException>(() => SettlementHaunting.StreetsDangerous(1291010263, false, value));
            }));
        }
    }
}
