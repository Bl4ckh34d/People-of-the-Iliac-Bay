using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class CounterServiceSlots
    {
        private sealed class Claim {public int Slot;public double Until;}
        private readonly List<GuardPoint3> slots;
        private readonly Dictionary<string,Claim> claims=new Dictionary<string,Claim>(StringComparer.Ordinal);
        public CounterServiceSlots(IList<GuardPoint3> slots)
        {if(slots==null) throw new ArgumentNullException(nameof(slots));this.slots=new List<GuardPoint3>();foreach(var p in slots)this.slots.Add(Copy(p));}
        public static List<GuardPoint3> Align(IList<GuardPoint3> candidates,GuardPoint3 keeper,GuardPoint3 normal)
        {
            if(candidates==null || keeper==null || normal==null) throw new ArgumentNullException();
            double length=Math.Sqrt(normal.X*normal.X+normal.Z*normal.Z);
            if(length<0.001) return new List<GuardPoint3>();
            double nx=normal.X/length,nz=normal.Z/length;
            var ordered=new List<GuardPoint3>(candidates);
            ordered.Sort((a,b)=>{int d=Square(a,keeper).CompareTo(Square(b,keeper));if(d!=0)return d;d=a.X.CompareTo(b.X);return d!=0?d:a.Z.CompareTo(b.Z);});
            var result=new List<GuardPoint3>();
            if(ordered.Count==0) return result;
            var anchor=ordered[0];double depth=(anchor.X-keeper.X)*nx+(anchor.Z-keeper.Z)*nz;
            foreach(var p in ordered)
            {
                if(Math.Abs(p.Y-anchor.Y)>0.35 || Math.Abs((p.X-keeper.X)*nx+(p.Z-keeper.Z)*nz-depth)>0.21) continue;
                bool near=false;
                foreach(var other in result)
                    if(Math.Abs((p.X-other.X)*nz-(p.Z-other.Z)*nx)<0.8-0.00001) {near=true;break;}
                if(!near) result.Add(Copy(p));
                if(result.Count>=6) break;
            }
            return result;
        }
        public bool TryClaim(string id,double now,int preferred,Func<GuardPoint3,bool> usable,out GuardPoint3 chosen)
        {
            if(string.IsNullOrEmpty(id) || double.IsNaN(now) || double.IsInfinity(now) || usable==null) throw new ArgumentException("Invalid counter claim.");
            foreach(var owner in new List<string>(claims.Keys)) if(claims[owner].Until<now) claims.Remove(owner);
            Claim current;
            if(claims.TryGetValue(id,out current))
            {current.Until=now+2;chosen=Copy(slots[current.Slot]);return true;}
            claims.Remove(id);
            for(int i=0;i<slots.Count;i++)
            {
                int index=((preferred%slots.Count)+slots.Count+i)%slots.Count;bool taken=false;
                foreach(var claim in claims.Values) if(claim.Slot==index) {taken=true;break;}
                if(taken || !usable(Copy(slots[index]))) continue;
                claims[id]=new Claim {Slot=index,Until=now+2};chosen=Copy(slots[index]);return true;
            }
            chosen=null;return false;
        }
        public void Release(string id) {if(id!=null) claims.Remove(id);}
        private static GuardPoint3 Copy(GuardPoint3 p) {return new GuardPoint3 {X=p.X,Y=p.Y,Z=p.Z};}
        private static double Square(GuardPoint3 a,GuardPoint3 b) {double x=a.X-b.X,z=a.Z-b.Z;return x*x+z*z;}
    }
}
