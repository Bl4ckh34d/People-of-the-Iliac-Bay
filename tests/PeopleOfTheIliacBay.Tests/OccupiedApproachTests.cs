using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class OccupiedApproachTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Occupied interior waypoint waits without oscillation and resumes when clear",()=>{
                var body=new GuardPoint3 {Z=-0.6};var target=new GuardPoint3 {Z=-0.2};
                var neighbours=new List<GuardPoint3> {new GuardPoint3()};
                int side=1;double offset=0.3;
                for(int frame=0;frame<300;frame++)
                {
                    body=CrowdSteering.MoveComfort(body,target,neighbours,0.52,1.1,1.8,0.02,
                        ref side,ref offset,new GuardPoint3 {Z=1},p=>true,true,true);
                    Check.Near(0,body.X);Check.Near(-0.6,body.Z);
                }
                Check.Equal(0,side);Check.Near(0,offset);
                neighbours.Clear();
                body=CrowdSteering.MoveComfort(body,target,neighbours,0.52,1.1,1.8,0.02,
                    ref side,ref offset,new GuardPoint3 {Z=1},p=>true,true,true);
                Check.True(body.Z>-0.6);
            }));
            cases.Add(("Waiting approach recovers existing overlap without entering walls",()=>{
                var body=new GuardPoint3 {Z=-0.5};var target=new GuardPoint3 {Z=-0.2};
                int side=0;double offset=0;
                var next=CrowdSteering.MoveComfort(body,target,new[]{new GuardPoint3()},0.52,1.1,1.8,0.04,
                    ref side,ref offset,null,p=>true,true,true);
                Check.True(next.Z<-0.5);
                next=CrowdSteering.MoveComfort(body,target,new[]{new GuardPoint3()},0.52,1.1,1.8,0.04,
                    ref side,ref offset,null,p=>false,true,true);
                Check.Near(body.Z,next.Z);
            }));
        }
    }
}
