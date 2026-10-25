using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class CounterServiceSlotsTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Counter slots spread along customer edge instead of behind one another",()=>{
                var floor=new List<GuardPoint3>();
                for(int depth=0;depth<5;depth++) for(int side=-3;side<=3;side++)
                    floor.Add(new GuardPoint3 {X=side*0.4,Z=1.6+depth*0.4});
                var slots=CounterServiceSlots.Align(floor,new GuardPoint3(),new GuardPoint3 {Z=1});
                Check.True(slots.Count>=3,"Counter has room for three customers side by side.");
                foreach(var spot in slots) Check.Near(1.6,spot.Z);
                for(int i=0;i<slots.Count;i++) for(int j=i+1;j<slots.Count;j++)
                    Check.True(Math.Abs(slots[i].X-slots[j].X)>=0.799,"Two customers share the same counter frontage.");
            }));
            cases.Add(("Counter frontage follows rotated geometry and excludes another floor",()=>{
                var slots=CounterServiceSlots.Align(new List<GuardPoint3>{
                    new GuardPoint3 {X=2,Z=0},new GuardPoint3 {X=2,Z=0.8},
                    new GuardPoint3 {X=2,Z=-0.8},new GuardPoint3 {X=2.8,Z=0},
                    new GuardPoint3 {X=2,Y=3,Z=1.6}},new GuardPoint3(),new GuardPoint3 {X=1});
                Check.Equal(3,slots.Count);
                foreach(var spot in slots) {Check.Near(2,spot.X);Check.Near(0,spot.Y);}
            }));
            cases.Add(("Delayed patrons claim distinct counter slots until service ends",()=>{
                var positions=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=0.8},new GuardPoint3{X=1.6}};
                var claims=new CounterServiceSlots(positions);GuardPoint3 a,b,c,d;
                Check.True(claims.TryClaim("a",10,0,p=>true,out a));
                Check.True(claims.TryClaim("b",10,0,p=>true,out b));
                Check.True(claims.TryClaim("c",10,0,p=>true,out c));
                Check.True(a.X!=b.X && a.X!=c.X && b.X!=c.X);
                Check.True(!claims.TryClaim("d",10,0,p=>true,out d),"Full counter must not create a spot behind another customer.");
                claims.Release("b");Check.True(claims.TryClaim("d",11,0,p=>true,out d));Check.Near(b.X,d.X);
                Check.True(claims.TryClaim("a",11,2,p=>true,out a));Check.Near(0,a.X);
                Check.True(claims.TryClaim("e",14,0,p=>true,out d),"Interrupted/departed actors must not reserve the counter forever.");
            }));
            cases.Add(("Counter claims skip blocked customer spots",()=>{
                var claims=new CounterServiceSlots(new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=1}});
                GuardPoint3 chosen;
                Check.True(claims.TryClaim("a",1,0,p=>p.X>0,out chosen));Check.Near(1,chosen.X);
                Check.True(!claims.TryClaim("b",1,0,p=>p.X>0,out chosen));
            }));
            cases.Add(("Passing bodies cannot steal or redirect a live counter claim",()=>{
                var claims=new CounterServiceSlots(new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=1}});
                GuardPoint3 chosen;Check.True(claims.TryClaim("a",1,0,p=>true,out chosen));
                Check.True(claims.TryClaim("a",1.5,1,p=>false,out chosen),"Temporary crowd overlap released a live reservation.");
                Check.Near(0,chosen.X);
            }));
        }
    }
}
