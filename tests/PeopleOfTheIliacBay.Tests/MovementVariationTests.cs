using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class MovementVariationTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Walking varies above and below baseline and remains stable per person",()=>{
                var speeds=new HashSet<double>(); double sum=0,minimum=double.MaxValue,maximum=0; bool slower=false,faster=false;
                for(uint seed=0;seed<10000;seed++)
                {
                    double speed=ResidentMovement.WalkSpeedFor(seed);
                    Check.True(speed>=Math.Round(4.2/3.6,4) && speed<=Math.Round(6.4/3.6,4));
                    Check.Near(speed,ResidentMovement.WalkSpeedFor(seed),0);
                    Check.Near(speed,Math.Round(speed,4),0);
                    speeds.Add(speed);sum+=speed;minimum=Math.Min(minimum,speed);maximum=Math.Max(maximum,speed);
                    slower|=speed<ResidentMovement.WalkSpeed;faster|=speed>ResidentMovement.WalkSpeed;
                }
                Check.True(slower && faster && speeds.Count>100);
                Check.True(minimum<=Math.Round(4.2/3.6,4)+0.001);
                Check.True(maximum>=Math.Round(6.4/3.6,4)-0.001);
                Check.Near((4.2/3.6+6.4/3.6)/2,sum/10000,0.01);
            }));
            cases.Add(("Existing fleeing variation stays stable and faster than walking",()=>{
                var speeds=new HashSet<double>();bool slower=false,faster=false;
                for(uint seed=0;seed<1000;seed++)
                {
                    double speed=ResidentMovement.FleeSpeed(seed);
                    Check.True(speed>=3.25 && speed<=4.55 && speed>ResidentMovement.WalkSpeedFor(seed));
                    uint oldSeed=StableSeed.Combine(seed,0,0,0x666c6565);
                    Check.Near(Math.Round(1.3*(2.5+(oldSeed%1001)/1000d),4),speed,0);
                    speeds.Add(speed);slower|=speed<3.9;faster|=speed>3.9;
                }
                Check.True(slower && faster && speeds.Count>100);
            }));
            cases.Add(("Personal walking speed controls fallback travel duration at 2x",()=>{
                foreach(uint seed in new uint[]{1,42,99})
                {
                    double speed=ResidentMovement.WalkSpeedFor(seed);
                    var schedule=DailySchedule.DayVisit(10,20,new[]{new RoutePoint(),new RoutePoint(130,0)},
                        600,1200,30,speed,2,0,360,1320);
                    Check.True(schedule.TryVisitWindowForBuilding(0,20,out double arrival,out double leave));
                    double start=arrival-130/speed*2;
                    Check.Near(speed*5,schedule.EvaluateSeconds(start+12).Position.X-schedule.EvaluateSeconds(start+2).Position.X);
                }
            }));
        }
    }
}
