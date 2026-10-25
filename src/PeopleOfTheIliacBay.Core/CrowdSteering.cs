using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    public static class CrowdSteering
    {
        public static GuardPoint3 MoveComfort(GuardPoint3 from,GuardPoint3 target,IEnumerable<GuardPoint3> neighbours,
            double separation,double comfortRadius,double lookAhead,double maximumStep,ref int passingSide,
            Func<GuardPoint3,bool> clear,bool continuePath=false)
        {
            double comfortOffset=0;
            return MoveComfort(from,target,neighbours,separation,comfortRadius,lookAhead,maximumStep,
                ref passingSide,ref comfortOffset,null,clear,continuePath);
        }
        public static GuardPoint3 MoveComfort(GuardPoint3 from,GuardPoint3 target,IEnumerable<GuardPoint3> neighbours,
            double separation,double comfortRadius,double lookAhead,double maximumStep,ref int passingSide,
            GuardPoint3 pathDirection,Func<GuardPoint3,bool> clear,bool continuePath=false)
        {
            double comfortOffset=0;
            return MoveComfort(from,target,neighbours,separation,comfortRadius,lookAhead,maximumStep,
                ref passingSide,ref comfortOffset,pathDirection,clear,continuePath);
        }
        public static GuardPoint3 MoveComfort(GuardPoint3 from,GuardPoint3 target,IEnumerable<GuardPoint3> neighbours,
            double separation,double comfortRadius,double lookAhead,double maximumStep,ref int passingSide,
            ref double comfortOffset,GuardPoint3 pathDirection,Func<GuardPoint3,bool> clear,bool continuePath=false,bool stopAtOccupiedTarget=false)
        {
            if(!Valid(from) || !Valid(target) || neighbours==null || clear==null || !Finite(separation) || separation<=0 ||
                !Finite(comfortRadius) || comfortRadius<=separation || !Finite(lookAhead) || lookAhead<=0 ||
                !Finite(maximumStep) || maximumStep<0 || passingSide < -1 || passingSide > 1 ||
                !Finite(comfortOffset) || comfortOffset<0 || comfortOffset>comfortRadius ||
                pathDirection!=null && !Valid(pathDirection))
                throw new ArgumentException("Invalid crowd comfort movement.");
            var others=ValidatedNeighbours(neighbours);
            if(stopAtOccupiedTarget && Occupied(target,others,separation))
            {
                passingSide=0;comfortOffset=0;
                foreach(var other in others)
                {
                    double x=from.X-other.X,z=from.Z-other.Z;
                    if(Math.Abs(other.Y-from.Y)>2.1 || x*x+z*z>=separation*separation-0.0000001) continue;
                    // Recover an existing overlap before waiting. The normal swept
                    // clearance and geometry checks still own the recovery step.
                    if(x*x+z*z<0.000001) x=1;
                    return Move(from,new GuardPoint3 {X=from.X+x,Y=from.Y,Z=from.Z+z},others,separation,maximumStep,clear);
                }
                return Copy(from);
            }
            double dx=target.X-from.X,dz=target.Z-from.Z,dy=target.Y-from.Y;
            double length=Math.Sqrt(dx*dx+dz*dz+dy*dy),horizontal=Math.Sqrt(dx*dx+dz*dz);
            if(length<0.000001 || horizontal<0.000001 || maximumStep==0)
            { passingSide=0; return Move(from,target,others,separation,maximumStep,clear); }
            double hx=dx/horizontal,hz=dz/horizontal;
            if(pathDirection!=null)
            {
                double pathHorizontal=Math.Sqrt(pathDirection.X*pathDirection.X+pathDirection.Z*pathDirection.Z);
                if(pathHorizontal>0.000001)
                { hx=pathDirection.X/pathHorizontal; hz=pathDirection.Z/pathHorizontal; }
            }
            double influence=0,chosenSide=0,chosenAhead=double.PositiveInfinity,chosenLateral=double.PositiveInfinity;
            foreach(var other in others)
            {
                if(Math.Abs(other.Y-from.Y)>2.1) continue;
                double rx=other.X-from.X,rz=other.Z-from.Z;
                double ahead=rx*hx+rz*hz;
                if(ahead<=0 || ahead>lookAhead || !continuePath && ahead>horizontal+separation) continue;
                double lateral=hx*rz-hz*rx;
                double sideDistance=Math.Abs(lateral);
                if(sideDistance>=comfortRadius) continue;
                double threat=Math.Max(0.01,(lookAhead-ahead)/lookAhead*(comfortRadius-sideDistance)/comfortRadius);
                bool stronger=threat>influence+0.0000001;
                bool stableTie=Math.Abs(threat-influence)<=0.0000001 &&
                    (ahead<chosenAhead-0.0000001 || Math.Abs(ahead-chosenAhead)<=0.0000001 && lateral<chosenLateral);
                if(!stronger && !stableTie) continue;
                influence=threat;
                chosenAhead=ahead; chosenLateral=lateral;
                chosenSide=lateral>0 ? -1 : lateral<0 ? 1 : 1;
            }
            bool conflict=influence>0;
            if(!conflict && passingSide!=0)
            {
                foreach(var other in others)
                {
                    if(Math.Abs(other.Y-from.Y)>2.1) continue;
                    double ahead=(other.X-from.X)*hx+(other.Z-from.Z)*hz;
                    if(ahead>0 && ahead<=lookAhead) { conflict=true; break; }
                }
            }
            if(conflict && passingSide==0) passingSide=(int)chosenSide;
            double desiredOffset=conflict ? comfortRadius*0.55 : 0;
            double offsetDelta=desiredOffset-comfortOffset;
            double offsetStep=Math.Min(maximumStep,0.12);
            comfortOffset+=Math.Max(-offsetStep,Math.Min(offsetStep,offsetDelta));
            if(comfortOffset<=0.000001) { comfortOffset=0; passingSide=0; }
            if(comfortOffset==0) return Move(from,target,others,separation,maximumStep,clear);
            double sideX=-hz*passingSide,sideZ=hx*passingSide;
            var shiftedTarget=new GuardPoint3 { X=target.X+sideX*comfortOffset,Y=target.Y,Z=target.Z+sideZ*comfortOffset };
            double tx=shiftedTarget.X-from.X,tz=shiftedTarget.Z-from.Z,ty=shiftedTarget.Y-from.Y;
            double shiftedLength=Math.Sqrt(tx*tx+ty*ty+tz*tz);
            if(shiftedLength<0.000001) return Copy(from);
            double step=Math.Min(Math.Min(length,maximumStep),shiftedLength);
            var candidate=new GuardPoint3 { X=from.X+tx/shiftedLength*step,Y=from.Y+ty/shiftedLength*step,Z=from.Z+tz/shiftedLength*step };
            if(Safe(from,candidate,others,separation) && clear(candidate)) return candidate;
            // Soft comfort yields to walls and tight passages; the original hard
            // separation search remains authoritative for the fallback step.
            return Move(from,target,others,separation,maximumStep,clear);
        }
        public static GuardPoint3 Move(GuardPoint3 from,GuardPoint3 target,IEnumerable<GuardPoint3> neighbours,
            double separation,double maximumStep,Func<GuardPoint3,bool> clear)
        {
            if(!Valid(from) || !Valid(target) || neighbours==null || clear==null || !Finite(separation) || separation<=0 ||
                !Finite(maximumStep) || maximumStep<0) throw new ArgumentException("Invalid crowd movement.");
            var others=ValidatedNeighbours(neighbours);
            double dx=target.X-from.X,dz=target.Z-from.Z,dy=target.Y-from.Y;
            double length=Math.Sqrt(dx*dx+dz*dz+dy*dy);
            if(length<0.000001 || maximumStep==0) return Copy(from);
            double step=Math.Min(length,maximumStep);
            foreach(double degrees in new[] { 0d,35,-35,70,-70,90,-90 })
            {
                double angle=degrees*Math.PI/180,cos=Math.Cos(angle),sin=Math.Sin(angle);
                var candidate=new GuardPoint3 { X=from.X+(dx*cos-dz*sin)/length*step,
                    Z=from.Z+(dx*sin+dz*cos)/length*step,Y=from.Y+dy/length*step };
                if(Safe(from,candidate,others,separation) && clear(candidate)) return candidate;
            }
            return Copy(from);
        }
        public static bool CanTraverse(GuardPoint3 from,GuardPoint3 target,IEnumerable<GuardPoint3> neighbours,
            double separation,Func<GuardPoint3,bool> clear)
        {
            if(!Valid(from) || !Valid(target) || neighbours==null || clear==null || !Finite(separation) || separation<=0)
                throw new ArgumentException("Invalid crowd traversal query.");
            var others=ValidatedNeighbours(neighbours);
            return Safe(from,target,others,separation) && clear(target);
        }
        private static bool Safe(GuardPoint3 from,GuardPoint3 candidate,IEnumerable<GuardPoint3> neighbours,double separation)
        {
            foreach(var other in neighbours)
            {
                if(Math.Abs(other.Y-candidate.Y)>2.1 && Math.Abs(other.Y-from.Y)>2.1) continue;
                double vx=candidate.X-from.X,vz=candidate.Z-from.Z;
                double square=vx*vx+vz*vz;
                double initialX=from.X-other.X,initialZ=from.Z-other.Z;
                double initialSquare=initialX*initialX+initialZ*initialZ;
                double minimumAllowed=separation*separation-0.0000001;
                if(initialSquare<minimumAllowed)
                {
                    // An existing overlap must be allowed to recover: requiring
                    // the whole sweep to clear separation makes t=0 reject every
                    // candidate. Permit only a non-closing path from this start.
                    double radialMotion=initialX*vx+initialZ*vz;
                    double finalX=candidate.X-other.X,finalZ=candidate.Z-other.Z;
                    double finalSquare=finalX*finalX+finalZ*finalZ;
                    if(radialMotion<-0.0000001 || finalSquare<initialSquare-0.0000001) return false;
                }
                else
                {
                    double t=square<0.000001 ? 0 : Math.Max(0,Math.Min(1,((other.X-from.X)*vx+(other.Z-from.Z)*vz)/square));
                    double x=from.X+vx*t-other.X,z=from.Z+vz*t-other.Z;
                    if(x*x+z*z<minimumAllowed) return false;
                }
            }
            return true;
        }
        public static bool Occupied(GuardPoint3 target,IEnumerable<GuardPoint3> neighbours,double separation)
        {
            if(!Valid(target) || !Finite(separation) || separation<=0) throw new ArgumentException("Invalid occupancy query.");
            foreach(var other in ValidatedNeighbours(neighbours))
            {
                double x=target.X-other.X,z=target.Z-other.Z;
                if(Math.Abs(target.Y-other.Y)<=2.1 && x*x+z*z<separation*separation) return true;
            }
            return false;
        }
        private static IList<GuardPoint3> ValidatedNeighbours(IEnumerable<GuardPoint3> neighbours)
        {
            if(neighbours==null) throw new ArgumentException("Invalid crowd movement.");
            IList<GuardPoint3> result=neighbours as IList<GuardPoint3>;
            if(result==null)
            {
                var copy=new List<GuardPoint3>();
                foreach(var other in neighbours) copy.Add(other);
                result=copy;
            }
            foreach(var other in result)
                if(!Valid(other)) throw new ArgumentException("Invalid crowd neighbour.");
            return result;
        }
        private static GuardPoint3 Copy(GuardPoint3 p) { return new GuardPoint3 { X=p.X,Y=p.Y,Z=p.Z }; }
        private static bool Valid(GuardPoint3 p) { return p!=null && Finite(p.X) && Finite(p.Y) && Finite(p.Z); }
        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
    }
}
