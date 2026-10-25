using System;
using System.Collections;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Read-only acceptance of the real native producer and GetState consumer.
    // No time changes, placements, publication calls, or save writes.
    internal static class HouseholdDayRuntimeProbe
    {
        internal static IEnumerator Run(PeopleOfTheIliacBayMod adapter,StreetPopulation streets,int settlement)
        {
            float deadline=Time.realtimeSinceStartup+90;
            while(Time.realtimeSinceStartup<deadline)
            {
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();int day=(int)Math.Floor(now/86400d);
                var snapshot=adapter.GetSaveData() as WorldState;
                if(snapshot!=null)
                {
                    var town=snapshot.Routines.Find(t=>t.SettlementId==settlement);
                    foreach(var plan in snapshot.HouseholdDays)
                    {
                        if(plan.SettlementId!=settlement || plan.AbsoluteDay!=day || plan.Status!="planned" ||
                            plan.JointInn!=null || plan.ChildIds.Count==0 || town==null)continue;
                        var first=town.Residents.Find(r=>r.ResidentId==plan.First.ResidentId);
                        var second=town.Residents.Find(r=>r.ResidentId==plan.Second.ResidentId);
                        if(first==null || second==null)continue;
                        var expectedA=HouseholdDayPlanner.Schedule(plan,plan.First.ResidentId,first);
                        var expectedB=HouseholdDayPlanner.Schedule(plan,plan.Second.ResidentId,second);
                        var baseA=ResidentRoutineDay.Schedule(plan.HomeKey,first,plan.WakeMinute,plan.SleepMinute);
                        var baseB=ResidentRoutineDay.Schedule(plan.HomeKey,second,plan.WakeMinute,plan.SleepMinute);
                        bool changed=false;int checks=0;
                        double start=Math.Max(plan.PlannedAt,day*86400d+plan.WakeMinute*60d),end=day*86400d+plan.SleepMinute*60d;
                        for(double at=start;at<end;at+=60)
                        {
                            var a=streets.GetState(plan.First.ResidentId,at);var b=streets.GetState(plan.Second.ResidentId,at);
                            var ea=expectedA.EvaluateSeconds(at);var eb=expectedB.EvaluateSeconds(at);
                            if(!Same(a,ea) || !Same(b,eb))throw new InvalidOperationException("Live household GetState differs from its published dated plan.");
                            if(!AtHome(a,plan.HomeKey) && !AtHome(b,plan.HomeKey))
                                throw new InvalidOperationException("Published native family outing leaves the child without either parent.");
                            if(!Same(a,baseA.EvaluateSeconds(at)) || !Same(b,baseB.EvaluateSeconds(at)))changed=true;
                            checks++;
                        }
                        if(!changed)continue;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_RUNTIME_PASS: home="+plan.HomeKey+"; day="+day+
                            "; parents=2; samples="+checks+"; livePublication=true; nativeGetState=true; changedFromBaseline=true; childcare=true; sharedLeisure=false; physicalWalking=false");
                        yield break;
                    }
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            throw new InvalidOperationException("No live dated two-parent household schedule replaced overlapping baseline outings.");
        }
        private static bool Same(ActivitySnapshot a,ActivitySnapshot b)
        {return a.Activity==b.Activity && a.BuildingKey==b.BuildingKey && Math.Abs(a.Position.X-b.Position.X)<0.0001 && Math.Abs(a.Position.Z-b.Position.Z)<0.0001;}
        private static bool AtHome(ActivitySnapshot value,int home)
        {return value.BuildingKey==home && (value.Activity==Activity.Home || value.Activity==Activity.Sleep);}
    }
}
