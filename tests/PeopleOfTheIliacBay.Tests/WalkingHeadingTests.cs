using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class WalkingHeadingTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Walking heading turns along shortest bounded arc across angle wrap",()=>{
                Check.Near(1.0134,Step(350,10,8,180,0.1,true),0.001);
                Check.Near(358.9866,Step(10,350,8,180,0.1,true),0.001);
                Check.Near(278.277287427,Step(90,270,8,180,1,true),1e-6);
            }));
            cases.Add(("Walking heading exponential response is frame-rate independent",()=>{
                double oneFrame=Step(0,30,8,1000,0.25,true);
                double splitFrames=0;
                for(int i=0;i<5;i++) splitFrames=Step(splitFrames,30,8,1000,0.05,true);
                Check.Near(30*(1-Math.Exp(-8*0.25)),oneFrame,1e-9);
                Check.Near(oneFrame,splitFrames,1e-9);
            }));
            cases.Add(("Walking heading remains frame-rate independent across turn-cap crossover",()=>{
                double oneFrame=Step(0,45,8,180,0.25,true);
                double splitFrames=0;
                for(int i=0;i<5;i++) splitFrames=Step(splitFrames,45,8,180,0.05,true);
                double threshold=180.0/8;
                double crossoverSeconds=(45-threshold)/180;
                double expected=45-threshold*Math.Exp(-8*(0.25-crossoverSeconds));
                Check.Near(expected,oneFrame,1e-9);
                Check.Near(oneFrame,splitFrames,1e-9);
            }));
            cases.Add(("Walking heading damps small alternating corrections",()=>{
                double heading=0;
                double minimum=double.MaxValue,maximum=double.MinValue;
                for(int i=0;i<180;i++){
                    heading=Step(heading,i%2==0?1:-1,8,1080,1.0/60,true);
                    if(i>=120){double signed=heading>180?heading-360:heading;minimum=Math.Min(minimum,signed);maximum=Math.Max(maximum,signed);}
                }
                Check.True(maximum-minimum<0.2,"alternating one-degree corrections should be strongly damped");
            }));
            cases.Add(("Walking heading caps large turns without snapping small corrections",()=>{
                Check.Near(1-Math.Exp(-8.0/60),Step(0,1,8,1080,1.0/60,true),1e-9);
                Check.Near(81,Step(90,270,8,90,0.1,true),1e-9);
            }));
            cases.Add(("Walking runtime turn cap permits brisk turns while preserving damping",()=>{
                double eased=Step(0,90,8,1080,0.1,true);
                Check.Near(90*(1-Math.Exp(-0.8)),eased,1e-9);
                Check.True(eased<90,"The increased cap must retain gradual easing rather than snap to target.");
                Check.True(eased<=1080*0.1,"The configured turn cap must bound each step.");
                Check.Near(327.6,Step(0,180,8,1080,0.03,true),1e-9);
            }));
            cases.Add(("Stationary and finished walkers use exact requested facing",()=>{
                Check.Near(90,Step(0,90,8,180,0,false),1e-9);
                Check.Near(90,Step(0,90,8,180,1,false),1e-9);
            }));
        }
        private static double Step(double current,double target,double response,double maxTurnRate,double deltaSeconds,bool smooth)
        {return WalkingHeading.StepDegrees(current,target,response,maxTurnRate,deltaSeconds,smooth);}
    }
}
