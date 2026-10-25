using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdJointSimulationStoreTests
    {
        private static object Call(WorldStateStore store,string method,params object[] args)
        {
            var api=typeof(WorldStateStore).GetMethod(method);
            if(api==null)throw new InvalidOperationException("Missing persisted household simulation: "+method);
            try{return api.Invoke(store,args);}
            catch(TargetInvocationException error){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        private static WorldStateStore Prepared(out HouseholdDayPlan plan,out HouseholdJointMovement actual)
        {
            var request=HouseholdDayAppointmentTests.SharedRequest();plan=HouseholdDayPlanTests.Create(request);
            var store=HouseholdDayPlanTests.Store(request);
            Check.True(store.TryPublishHouseholdDay(plan,request.PlannedAt,out var reason),reason);
            actual=HouseholdJointMovement.Create(plan,plan.PlannedAt);store.SaveHouseholdJointProgress(actual.Snapshot());return store;
        }
        private static double Departure(HouseholdDayPlan plan){return plan.AbsoluteDay*86400d+18*3600;}
        private static SavedRoutePoint Point(double x){return new SavedRoutePoint{X=x};}
        private static HouseholdJointSimulation Begin(WorldStateStore store,HouseholdDayPlan plan,double at,int seq=0)
        {return (HouseholdJointSimulation)Call(store,"BeginHouseholdJointSimulation",plan.Id,seq,at,null);}
        private static HouseholdJointSimulation Advance(WorldStateStore store,HouseholdDayPlan plan,double at,bool a=true,bool b=true,bool paused=false)
        {return (HouseholdJointSimulation)Call(store,"AdvanceHouseholdJointSimulation",plan.Id,at,a,b,paused,null);}
        private static HouseholdJointSimulation Read(WorldStateStore store)
        {return (HouseholdJointSimulation)Call(store,"ReadHouseholdJointSimulation",2,7);}
        private static IList Ledger(WorldState saved)
        {
            var field=typeof(WorldState).GetField("HouseholdJointSimulations");
            if(field==null)throw new InvalidOperationException("Missing household simulation save ledger.");
            return (IList)field.GetValue(saved);
        }
        private static object Placement(string id,double x)
        {
            var type=typeof(WorldState).Assembly.GetType("PeopleOfTheIliacBay.Core.HouseholdJointPlacement");
            if(type==null)throw new InvalidOperationException("Missing explicit paired materialization API.");
            var value=Activator.CreateInstance(type);type.GetField("ResidentId").SetValue(value,id);
            type.GetField("Position").SetValue(value,Point(x));return value;
        }
        private static HouseholdJointProgress Reconstruct(WorldStateStore store,HouseholdDayPlan plan,int revision,double now,
            double x,double y,string a="mother",string b="father")
        {return (HouseholdJointProgress)Call(store,"ReconstructHouseholdJointSimulation",plan.Id,revision,now,Placement(a,x),Placement(b,y),null);}
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Moving recovery cannot be erased by materialization after a duty disappears",()=>{
                var store=Prepared(out var plan,out _);double at=Departure(plan);Begin(store,plan,at);Advance(store,plan,at+150);
                var booking=new AppointmentRecord{Id="persistent-moving-recovery",ResidentId="father",Purpose="meet",Status="accepted",
                    StartsAt=at+600,EndsAt=at+1200,Importance=2,Place=new AppointmentPlace{Kind="building",SettlementId=2,BuildingKey=10}};
                Check.True(store.TryReserveAppointment(booking,at+151,out var reason),reason);Advance(store,plan,at+160);
                var saved=store.Snapshot();saved.Appointments.Clear();store.Restore(saved);var recovery=Read(store);
                Check.True(recovery.RequiresRecovery);
                Check.Throws<ArgumentException>(()=>Reconstruct(store,plan,recovery.Revision,at+160,5,5));
                Check.True(Read(store).Active);Check.Equal(0,store.ReadHouseholdJointProgress(2,7).Sequence);
            }));
            cases.Add(("Interior Visiting anchor cannot reconstruct from exterior route points",()=>{
                var store=Prepared(out var plan,out var body);double at=Departure(plan);body.Depart(at,Point(0),Point(0));store.SaveHouseholdJointProgress(body.Snapshot());
                for(int i=1;i<=20;i++){body.Observe(at+i,Point(i*0.5),Point(i*0.5));store.SaveHouseholdJointProgress(body.Snapshot());}
                body.EnterInn(at+21);store.SaveHouseholdJointProgress(body.Snapshot());
                var sim=Begin(store,plan,at+22,body.Snapshot().Sequence);
                Check.Throws<ArgumentException>(()=>Reconstruct(store,plan,sim.Revision,at+22,10,10));
                Check.True(Read(store).Active);Check.Near(at+21,store.ReadHouseholdJointProgress(2,7).EnteredAt);
                Check.Equal(HouseholdJointPhase.Visiting,store.ReadHouseholdJointProgress(2,7).Phase);
            }));
            cases.Add(("Materialization rejects invalid coordinates and respects its placement tolerance",()=>{
                var store=Prepared(out var plan,out _);double at=Departure(plan);Begin(store,plan,at);var sim=Advance(store,plan,at+150);
                Check.Throws<ArgumentException>(()=>Reconstruct(store,plan,sim.Revision,at+150,double.NaN,5));
                Check.Throws<ArgumentException>(()=>Reconstruct(store,plan,sim.Revision,at+150,5.55001,5));
                Check.Throws<ArgumentException>(()=>Reconstruct(store,plan,sim.Revision,at+149,5,5));
                Check.Equal(0,store.ReadHouseholdJointProgress(2,7).Sequence);Check.True(Read(store).Active);
                Check.Near(5.55,Reconstruct(store,plan,sim.Revision,at+150,5.55,5).First.Position.X);
            }));
            cases.Add(("An active handoff retry cannot revive a stale routine or dead parent",()=>{
                foreach(bool death in new[]{false,true})
                {
                    var store=Prepared(out var plan,out _);double at=Departure(plan);Begin(store,plan,at);
                    var snapshot=store.Snapshot();
                    if(death)snapshot.Settlements[0].Households[0].Residents[0].IsAlive=false;
                    else snapshot.Routines[0].Residents[0].OpenMinute++;
                    store.Restore(snapshot);
                    Check.Throws<ArgumentException>(()=>Begin(store,plan,at+1));
                    var frozen=Advance(store,plan,at+1);
                    Check.Near(0,frozen.First.Distance);Check.Near(at+1,Read(store).AdvancedThrough);
                    if(!death)Check.True(frozen.RequiresRecovery);
                    Check.Near(0,Read(store).Base.First.Distance);
                }
            }));
            cases.Add(("Unknown appointment route consumes held time without simulated or actual movement",()=>{
                var store=Prepared(out var plan,out _);double at=Departure(plan);Begin(store,plan,at);
                var booking=new AppointmentRecord{Id="unknown-projected-route",ResidentId="father",Purpose="meet",Status="accepted",
                    StartsAt=at+600,EndsAt=at+1200,Importance=2,
                    Place=new AppointmentPlace{Kind="world-point",SettlementId=2,BuildingKey=-1,X=10,Y=0,Z=0}};
                Check.True(store.TryReserveAppointment(booking,at+1,out var reason),reason);
                var held=Advance(store,plan,at+100);
                Check.Equal("route-proof-unavailable",held.PauseReason);Check.Near(at+100,Read(store).AdvancedThrough);
                Check.Near(0,held.First.Distance);
                Check.Equal(0,store.ReadHouseholdJointProgress(2,7).Sequence);
                var saved=store.Snapshot();saved.Appointments.Clear();store.Restore(saved);
                Check.Near(1,Advance(store,plan,at+130).Second.Distance);
            }));
            cases.Add(("Native or protected priority consumes held time before executable source resolution",()=>{
                foreach(bool native in new[]{false,true})
                {
                    var store=Prepared(out var plan,out _);double at=Departure(plan);Begin(store,plan,at);
                    var parent=store.ResolveResident("father");
                    if(native)parent.NativeIdentity=new NativePerson();else parent.IsQuestProtected=true;
                    var held=Advance(store,plan,at+100);Check.Near(0,held.Second.Distance);Check.Near(at+100,held.AdvancedThrough);
                    parent.NativeIdentity=null;parent.IsQuestProtected=false;
                    Check.Near(1,Advance(store,plan,at+130).First.Distance);
                }
            }));
            cases.Add(("Active simulation cannot load against missing or replaced canonical route source",()=>{
                var store=Prepared(out var plan,out _);double at=Departure(plan);Begin(store,plan,at);
                var saved=store.Snapshot();
                foreach(var outing in new[]{saved.HouseholdDays[0].JointInn.First,saved.HouseholdDays[0].JointInn.Second})
                {outing.Itinerary.Stops[0].Route[1].X=11;outing.Itinerary.ExitRoute[0].X=11;}
                Check.Throws<ArgumentException>(()=>store.Restore(saved));Check.True(Read(store).Active);
                saved=store.Snapshot();saved.HouseholdDays.Clear();Check.Throws<ArgumentException>(()=>store.Restore(saved));
                Check.Near(at,Read(store).AdvancedThrough);
            }));
            cases.Add(("Missed waiting excursion can hand back only two actual home placements",()=>{
                var store=Prepared(out var plan,out _);double at=Departure(plan);Begin(store,plan,at);
                Advance(store,plan,at+4000,b:false);var expired=Advance(store,plan,at+4100);
                Check.True(expired.RequiresRecovery);Check.Equal(HouseholdJointPhase.Waiting,expired.Phase);
                Check.Throws<ArgumentException>(()=>Reconstruct(store,plan,expired.Revision,at+4100,0,1));
                var home=Reconstruct(store,plan,expired.Revision,at+4100,0,0);
                Check.Equal(HouseholdJointPhase.Waiting,home.Phase);Check.Near(0,home.DepartedAt);
                Check.True(store.TryRetireHouseholdJointAtHome(2,7,at+4101,id=>true));
                Check.True(!store.HasActiveHouseholdJoint(2,"mother"));
            }));
            cases.Add(("Native serializer preserves active and consumed projection ownership",()=>{
                var store=Prepared(out var plan,out _);double at=Departure(plan);Begin(store,plan,at);var simulated=Advance(store,plan,at+150);
                var serializer=new FullSerializer.fsSerializer();FullSerializer.fsData encoded;
                Check.True(serializer.TrySerialize(typeof(WorldState),store.Snapshot(),out encoded).Succeeded);
                object decoded=null;Check.True(serializer.TryDeserialize(FullSerializer.fsJsonParser.Parse(FullSerializer.fsJsonPrinter.CompressedJson(encoded)),typeof(WorldState),ref decoded).Succeeded);
                var restored=new WorldStateStore(0);restored.Restore((WorldState)decoded);
                Check.True(Read(restored).Active);Check.True(restored.GetHouseholdJointMovement(2,7)==null);
                Reconstruct(restored,plan,simulated.Revision,at+150,5,5);
                Check.True(serializer.TrySerialize(typeof(WorldState),restored.Snapshot(),out encoded).Succeeded);decoded=null;
                Check.True(serializer.TryDeserialize(encoded,typeof(WorldState),ref decoded).Succeeded);restored.Restore((WorldState)decoded);
                Check.True(!Read(restored).Active);Check.Near(at+150,Read(restored).ReconstructedAt.Value);
                Check.Near(5,restored.GetHouseholdJointMovement(2,7).Snapshot().First.Distance);
            }));
            cases.Add(("Either parent's observed injury blocks or pauses simulated leisure",()=>{
                foreach(string id in new[]{"mother","father"})
                {
                    var store=Prepared(out var plan,out _);double at=Departure(plan);Begin(store,plan,at);
                    var vitals=store.ReadNpcVitals(id,NpcArchetype.Citizen);
                    Check.True(store.TryCaptureNpcVitals(id,NpcArchetype.Citizen,1,vitals.CurrentMagicka,at+1,vitals.Revision));
                    Check.True(store.TryBeginNpcHealthCare(id,NpcArchetype.Citizen,at+1));
                    Check.Near(0,Advance(store,plan,at+150).First.Distance);
                    Check.Throws<ArgumentException>(()=>Reconstruct(store,plan,Read(store).Revision,at+150,0,0));
                    var newStore=Prepared(out var secondPlan,out _);vitals=newStore.ReadNpcVitals(id,NpcArchetype.Citizen);
                    Check.True(newStore.TryCaptureNpcVitals(id,NpcArchetype.Citizen,1,vitals.CurrentMagicka,at,vitals.Revision));
                    Check.True(newStore.TryBeginNpcHealthCare(id,NpcArchetype.Citizen,at));
                    Check.Throws<ArgumentException>(()=>Begin(newStore,secondPlan,at));
                }
            }));
            cases.Add(("Consumed projection at valid zero world time cannot regain ownership",()=>{
                Check.True(!new HouseholdJointSimulation{ReconstructedAt=0}.Active);
            }));
            cases.Add(("Stored projection owns both parents without rewriting actual movement",()=>{
                var store=Prepared(out var plan,out var actual);double at=Departure(plan);
                Begin(store,plan,at);var projected=Advance(store,plan,at+150);
                Check.Near(5,projected.First.Position.X);Check.Near(0,store.ReadHouseholdJointProgress(2,7).First.Position.X);
                Check.True(store.GetHouseholdJointMovement(2,7)==null);
                Check.Throws<ArgumentException>(()=>store.SaveHouseholdJointProgress(actual.Snapshot()));
                Check.True(!store.TryRetireHouseholdJointAtHome(2,7,at+7200,id=>true));
                Check.True(store.HasActiveHouseholdJoint(2,"mother"));Check.True(store.HasActiveHouseholdJoint(2,"father"));
                projected.First.Position.X=90;projected.Base.First.Position.X=90;
                Check.Near(5,Read(store).First.Position.X);Check.Near(0,Read(store).Base.First.Position.X);
            }));
            cases.Add(("Simulation handoff checks physical sequence and begins without retroactive movement",()=>{
                var store=Prepared(out var plan,out var actual);double at=Departure(plan);
                actual.Depart(at,Point(0),Point(0));store.SaveHouseholdJointProgress(actual.Snapshot());
                Check.Throws<ArgumentException>(()=>Begin(store,plan,at+100));Check.True(Read(store)==null);
                var start=Begin(store,plan,at+100,1);var retry=Begin(store,plan,at+120,1);
                Check.Near(at+100,retry.StartedAt);Check.Equal(start.Revision,retry.Revision);
                Check.Near(1,Advance(store,plan,at+130).First.Position.X);
                Check.Throws<ArgumentException>(()=>Call(store,"AdvanceHouseholdJointSimulation","other-day",at+200,true,true,false,null));
                Check.Near(at+130,Read(store).AdvancedThrough);
            }));
            cases.Add(("Projection and actual anchor roundtrip with one active owner",()=>{
                var store=Prepared(out var plan,out _);double at=Departure(plan);Begin(store,plan,at);Advance(store,plan,at+150);
                var options=new JsonSerializerOptions{IncludeFields=true};var restored=new WorldStateStore(0);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(store.Snapshot(),options),options));
                Check.Near(5,Read(restored).Second.Distance);Check.True(restored.GetHouseholdJointMovement(2,7)==null);
                Check.Near(6,Advance(restored,plan,at+180).First.Distance);
                var bad=store.Snapshot();Ledger(bad).Add(HouseholdJointSimulation.Copy(Read(store)));
                Check.Throws<ArgumentException>(()=>restored.Restore(bad));Check.Near(6,Read(restored).First.Distance);
                bad=store.Snapshot();bad.HouseholdJointProgress[0].ObservedAt++;
                Check.Throws<ArgumentException>(()=>restored.Restore(bad));
            }));
            cases.Add(("Schema71 adds no inferred projection and rejects future simulation fields",()=>{
                var store=Prepared(out var plan,out _);double at=Departure(plan);Begin(store,plan,at);
                var saved=store.Snapshot();saved.Version=71;var restored=new WorldStateStore(0);
                Check.Throws<ArgumentException>(()=>restored.Restore(saved));Ledger(saved).Clear();restored.Restore(saved);
                Check.True(Read(restored)==null);Check.True(restored.GetHouseholdJointMovement(2,7)!=null);
            }));
            cases.Add(("Either parent's required appointment prevents handing leisure to simulation",()=>{
                foreach(string id in new[]{"mother","father"})
                {
                    var store=Prepared(out var plan,out _);double at=Departure(plan);
                    var booking=new AppointmentRecord{Id="simulation-duty-"+id,ResidentId=id,Purpose="meet",Status="accepted",
                        StartsAt=at+600,EndsAt=at+1200,Importance=2,Place=new AppointmentPlace{Kind="building",SettlementId=2,BuildingKey=10}};
                    Check.True(store.TryReserveAppointment(booking,plan.PlannedAt+1,out var reason),reason);
                    Check.Throws<ArgumentException>(()=>Begin(store,plan,at));Check.True(Read(store)==null);
                }
            }));
            cases.Add(("New duty freezes projected outward travel into durable recovery",()=>{
                var store=Prepared(out var plan,out _);double at=Departure(plan);Begin(store,plan,at);Advance(store,plan,at+150);
                var booking=new AppointmentRecord{Id="projected-duty",ResidentId="father",Purpose="meet",Status="accepted",
                    StartsAt=at+600,EndsAt=at+1200,Importance=2,Place=new AppointmentPlace{Kind="building",SettlementId=2,BuildingKey=10}};
                Check.True(store.TryReserveAppointment(booking,at+151,out var reason),reason);
                var held=Advance(store,plan,at+180);Check.True(held.RequiresRecovery);Check.Near(5,held.First.Distance);
                Check.Near(5,Advance(store,plan,at+300).Second.Distance);Check.True(Read(store).Active);
            }));
            cases.Add(("Explicit pair materialization atomically consumes projection and restores real movement",()=>{
                var store=Prepared(out var plan,out _);double at=Departure(plan);Begin(store,plan,at);
                var projected=Advance(store,plan,at+150);int revision=projected.Revision;
                Check.Throws<ArgumentException>(()=>Reconstruct(store,plan,revision+1,at+150,5,5));
                Check.Throws<ArgumentException>(()=>Reconstruct(store,plan,revision,at+150,5,8));
                Check.Throws<ArgumentException>(()=>Reconstruct(store,plan,revision,at+150,5,5,"father","mother"));
                Check.True(Read(store).Active);Check.Equal(0,store.ReadHouseholdJointProgress(2,7).Sequence);
                var actual=Reconstruct(store,plan,revision,at+150,5,5);
                Check.Equal(1,actual.Sequence);Check.Equal(HouseholdJointPhase.Outbound,actual.Phase);Check.Near(at+150,actual.ObservedAt);
                Check.True(!Read(store).Active);Check.Near(0,Read(store).Base.First.Distance);
                Check.Near(5,store.GetHouseholdJointMovement(2,7).Snapshot().First.Distance);
                Check.Equal(1,Reconstruct(store,plan,revision,at+150,5,5).Sequence);
                var body=store.GetHouseholdJointMovement(2,7);body.Observe(at+151,Point(5.5),Point(5.5));store.SaveHouseholdJointProgress(body.Snapshot());
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());Check.Near(5.5,restored.ReadHouseholdJointProgress(2,7).First.Distance);
                Check.Throws<ArgumentException>(()=>Advance(store,plan,at+200));
            }));
            cases.Add(("Materialized virtual Inn arrival does not prove entry or purchases",()=>{
                var store=Prepared(out var plan,out _);double at=Departure(plan);Begin(store,plan,at);
                var projected=Advance(store,plan,at+300);var actual=Reconstruct(store,plan,projected.Revision,at+300,10,10);
                Check.Equal(HouseholdJointPhase.AtInn,actual.Phase);Check.Near(0,actual.EnteredAt);
                Check.Equal(0,store.Snapshot().ObservedVisits.Count);Check.Equal(0,store.Snapshot().NpcInnConsumptionCursors.Count);
            }));
            cases.Add(("Projected home completion materializes return without inventing observed completion",()=>{
                var store=Prepared(out var plan,out var body);double at=Departure(plan);body.Depart(at,Point(0),Point(0));store.SaveHouseholdJointProgress(body.Snapshot());
                for(int i=1;i<=20;i++){body.Observe(at+i,Point(i*0.5),Point(i*0.5));store.SaveHouseholdJointProgress(body.Snapshot());}
                body.BeginReturn(at+21,Point(10),Point(10));store.SaveHouseholdJointProgress(body.Snapshot());
                Begin(store,plan,at+21,body.Snapshot().Sequence);var projected=Advance(store,plan,at+321);
                Check.Equal(HouseholdJointPhase.Completed,projected.Phase);
                var actual=Reconstruct(store,plan,projected.Revision,at+321,0,0);Check.Equal(HouseholdJointPhase.Returning,actual.Phase);
                Check.True(store.HasActiveHouseholdJoint(2,"mother"));body=store.GetHouseholdJointMovement(2,7);
                body.Observe(at+322,Point(0),Point(0));store.SaveHouseholdJointProgress(body.Snapshot());
                Check.True(!store.HasActiveHouseholdJoint(2,"mother"));
            }));
        }
    }
}
