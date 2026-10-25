using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdJointSimulationTests
    {
        private static object Call(string method,params object[] arguments)
        {
            var type=typeof(WorldState).Assembly.GetType("PeopleOfTheIliacBay.Core.HouseholdJointSimulation");
            if(type==null)throw new InvalidOperationException("Missing separate household simulation API.");
            try{return type.GetMethod(method,BindingFlags.Public|BindingFlags.Static).Invoke(null,arguments);}
            catch(TargetInvocationException error){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        private static T Field<T>(object value,string field){return (T)value.GetType().GetField(field).GetValue(value);}
        private static SavedRoutePoint Position(object value,string member){return Field<SavedRoutePoint>(Field<object>(value,member),"Position");}
        private static double Distance(object value,string member){return Field<double>(Field<object>(value,member),"Distance");}
        private static double Departure(HouseholdDayPlan plan){return plan.AbsoluteDay*86400d+18*3600;}
        private static HouseholdDayPlan Plan(){return HouseholdJointMovementTests.Plan();}
        private static object Begin(HouseholdDayPlan plan,double at,out HouseholdJointProgress actual)
        {actual=HouseholdJointMovement.Create(plan,plan.PlannedAt).Snapshot();return Call("Begin",plan,actual,at);}
        private static object Advance(HouseholdDayPlan plan,object projection,double at,bool a=true,bool b=true,bool paused=false)
        {return Call("Advance",plan,projection,at,a,b,paused);}
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Late handoff immediately requests recovery even without a later clock tick",()=>{
                var plan=Plan();double at=Departure(plan)+4000;var start=(HouseholdJointSimulation)Begin(plan,at,out _);
                Check.True(start.RequiresRecovery);Check.Equal(HouseholdJointPhase.Waiting,start.Phase);
                Check.True(((HouseholdJointSimulation)Advance(plan,start,at)).RequiresRecovery);
                Check.Near(0,start.DepartedAt);Check.Near(0,start.First.Distance);
            }));
            cases.Add(("Resuming at virtual Inn clears temporary pause without fabricating service",()=>{
                var plan=Plan();double at=Departure(plan);var start=Begin(plan,at,out _);
                var arrived=Advance(plan,start,at+300);
                var paused=(HouseholdJointSimulation)Advance(plan,arrived,at+400,paused:true);
                Check.Equal("game-paused",paused.PauseReason);
                var resumed=(HouseholdJointSimulation)Advance(plan,paused,at+500);
                Check.Equal<string>(null,resumed.PauseReason);Check.Equal(HouseholdJointPhase.AtInn,resumed.Phase);
                Check.Near(at+400,resumed.AdvancedThrough);Check.Near(0,resumed.Base.EnteredAt);
            }));
            cases.Add(("Missed leisure window does not invent a late virtual departure",()=>{
                var plan=Plan();double at=Departure(plan);var start=Begin(plan,at+4000,out _);
                var result=Advance(plan,start,at+4100);
                Check.Equal(HouseholdJointPhase.Waiting,Field<HouseholdJointPhase>(result,"Phase"));
                Check.Near(0,Field<double>(result,"DepartedAt"));Check.Near(0,Distance(result,"First"));
                Check.Equal("leisure-expired",Field<string>(result,"PauseReason"));
            }));
            cases.Add(("Projected leader waits for lagging parent without resetting real route history",()=>{
                var plan=Plan();double at=Departure(plan);var motion=HouseholdJointMovement.Create(plan,plan.PlannedAt);
                motion.Depart(at,new SavedRoutePoint(),new SavedRoutePoint());
                for(int i=1;i<=6;i++)motion.Observe(at+i,new SavedRoutePoint{X=i*0.5},new SavedRoutePoint());
                var actual=motion.Snapshot();var start=Call("Begin",plan,actual,at+6);
                var result=Advance(plan,start,at+36);Check.Near(3,Distance(result,"First"));Check.Near(1,Distance(result,"Second"));
                result=Advance(plan,result,at+126);Check.Near(5.5,Distance(result,"First"));Check.Near(4,Distance(result,"Second"));
                Check.Near(3,actual.First.Distance);Check.Near(0,actual.Second.Distance);
            }));
            cases.Add(("Equal-time retry is idempotent and unsupported projection model cannot resume",()=>{
                var plan=Plan();double at=Departure(plan);var start=Begin(plan,at,out _);
                var same=Advance(plan,start,at);Check.Equal(0,Field<int>(same,"Revision"));
                same.GetType().GetField("ModelVersion").SetValue(same,2);
                Check.Throws<ArgumentException>(()=>Advance(plan,same,at+1));Check.Equal(1,Field<int>(start,"ModelVersion"));
            }));
            cases.Add(("Unloaded pair projects measured travel without rewriting actual observations",()=>{
                var plan=Plan();double departure=Departure(plan);var projection=Begin(plan,departure,out var actual);
                var result=Advance(plan,projection,departure+150);
                Check.Equal("world-time-projection",Field<string>(result,"Provenance"));
                Check.Equal(HouseholdJointPhase.Outbound,Field<HouseholdJointPhase>(result,"Phase"));
                Check.Near(5,Position(result,"First").X);
                Check.Near(5,Position(result,"Second").X);
                Check.Equal(HouseholdJointPhase.Waiting,actual.Phase);Check.Equal(0,actual.Sequence);Check.Near(0,actual.First.Position.X);
                Check.Equal(actual.ObservedAt,Field<HouseholdJointProgress>(result,"Base").ObservedAt);
                Check.Near(0,Position(projection,"First").X);
            }));
            cases.Add(("Slower parent's measured route duration limits both simulated walkers",()=>{
                var plan=Plan();plan.JointInn.Second.Itinerary.Stops[0].WalkSeconds=600;
                double at=Departure(plan);var projection=Begin(plan,at,out _);var result=Advance(plan,projection,at+150);
                Check.Near(2.5,Distance(result,"First"));
                Check.Near(2.5,Distance(result,"Second"));
            }));
            cases.Add(("Simulated Inn arrival retains unconsumed time without inventing physical entry",()=>{
                var plan=Plan();double at=Departure(plan);var projection=Begin(plan,at,out var actual);
                var result=Advance(plan,projection,at+1000);
                Check.Equal(HouseholdJointPhase.AtInn,Field<HouseholdJointPhase>(result,"Phase"));
                Check.Near(at+300,Field<double>(result,"AdvancedThrough"));Check.Near(at+300,Field<double>(result,"PhaseSince"));
                Check.Equal(0d,actual.EnteredAt);Check.Equal(HouseholdJointPhase.Waiting,Field<HouseholdJointProgress>(result,"Base").Phase);
                var held=Advance(plan,result,at+2000);Check.Near(at+300,Field<double>(held,"AdvancedThrough"));
                Check.Equal(Field<int>(result,"Revision"),Field<int>(held,"Revision"));
            }));
            cases.Add(("Unavailable partner consumes held time without later simulated catch-up",()=>{
                var plan=Plan();double at=Departure(plan);var projection=Begin(plan,at,out _);
                projection=Advance(plan,projection,at+100,b:false);
                Check.Near(0,Distance(projection,"First"));
                var resumed=Advance(plan,projection,at+130);
                Check.Near(1,Distance(resumed,"First"));
                Check.Near(at+100,Field<double>(resumed,"DepartedAt"));
            }));
            cases.Add(("Pause and future departure do not move simulated parents before a shared outing",()=>{
                var plan=Plan();double at=Departure(plan);var projection=Begin(plan,at-100,out _);
                projection=Advance(plan,projection,at-50);Check.Equal(HouseholdJointPhase.Waiting,Field<HouseholdJointPhase>(projection,"Phase"));
                projection=Advance(plan,projection,at+100,paused:true);
                Check.Near(0,Distance(projection,"Second"));
                projection=Advance(plan,projection,at+130);Check.Near(1,Distance(projection,"Second"));
            }));
            cases.Add(("Returning simulation uses saved return duration and preserves actual source anchor",()=>{
                var plan=Plan();double at=Departure(plan);var motion=HouseholdJointMovement.Create(plan,plan.PlannedAt);
                motion.Depart(at,new SavedRoutePoint(),new SavedRoutePoint());
                for(int i=1;i<=20;i++)motion.Observe(at+i,new SavedRoutePoint{X=i*0.5},new SavedRoutePoint{X=i*0.5});
                motion.EnterInn(at+21);motion.BeginReturn(at+22,new SavedRoutePoint{X=10},new SavedRoutePoint{X=10});
                var actual=motion.Snapshot();var projection=Call("Begin",plan,actual,at+22);
                var halfway=Advance(plan,projection,at+172);Check.Near(5,Position(halfway,"First").X);
                var done=Advance(plan,halfway,at+1000);Check.Equal(HouseholdJointPhase.Completed,Field<HouseholdJointPhase>(done,"Phase"));
                Check.Near(at+322,Field<double>(done,"AdvancedThrough"));Check.Equal(HouseholdJointPhase.Returning,actual.Phase);
                Check.Near(10,actual.First.Position.X);
            }));
            cases.Add(("Large and incremental unloaded travel agree and expose no mutable shared cursors",()=>{
                var plan=Plan();double at=Departure(plan);var start=Begin(plan,at,out _);
                var direct=Advance(plan,start,at+240);object incremental=start;
                for(int i=1;i<=24;i++)incremental=Advance(plan,incremental,at+i*10);
                Check.Near(8,Distance(direct,"First"));
                Check.Near(8,Distance(incremental,"First"));
                Position(direct,"First").X=99;
                Check.Near(8,Position(incremental,"First").X);
                Check.Near(0,Field<HouseholdJointProgress>(incremental,"Base").First.Position.X);
            }));
            cases.Add(("Unloaded projection refuses source changes and reverse clocks without altering inputs",()=>{
                var plan=Plan();double at=Departure(plan);var start=Begin(plan,at,out _);
                Check.Throws<ArgumentException>(()=>Advance(plan,start,at-1));
                Check.Throws<ArgumentException>(()=>Advance(plan,start,double.NaN));
                plan.JointInn.First.Itinerary.Stops[0].Route[1].X=11;
                Check.Throws<ArgumentException>(()=>Advance(plan,start,at+10));
                Check.Near(at,Field<double>(start,"AdvancedThrough"));Check.Near(0,Distance(start,"First"));
            }));
        }
    }
}
