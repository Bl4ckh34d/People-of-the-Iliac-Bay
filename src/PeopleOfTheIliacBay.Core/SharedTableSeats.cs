using System;

namespace PeopleOfTheIliacBay.Core
{
    // Scene geometry only. A table link is established from actual furniture,
    // never from an appointment or matching arrival times.
    public sealed class TableSeatPosition
    {
        public string Id,TableId;
        public double X,Z,Height,FacingX,FacingZ;
    }

    public static class SharedTableSeats
    {
        public static bool CanShare(TableSeatPosition first,TableSeatPosition second)
        {
            if(!Valid(first) || !Valid(second) || first.Id==second.Id || first.TableId!=second.TableId ||
                Math.Abs(first.Height-second.Height)>0.4)return false;
            double x=second.X-first.X,z=second.Z-first.Z,square=x*x+z*z;
            if(square<0.6*0.6 || square>3.5*3.5)return false;
            double distance=Math.Sqrt(square);
            double firstLength=Math.Sqrt(first.FacingX*first.FacingX+first.FacingZ*first.FacingZ);
            double secondLength=Math.Sqrt(second.FacingX*second.FacingX+second.FacingZ*second.FacingZ);
            return firstLength>0.001 && secondLength>0.001 &&
                (x*first.FacingX+z*first.FacingZ)/(distance*firstLength)>=0.25 &&
                (-x*second.FacingX-z*second.FacingZ)/(distance*secondLength)>=0.25;
        }
        private static bool Valid(TableSeatPosition seat)
        {return seat!=null && !string.IsNullOrWhiteSpace(seat.Id) && !string.IsNullOrWhiteSpace(seat.TableId) &&
            Finite(seat.X) && Finite(seat.Z) && Finite(seat.Height) && Finite(seat.FacingX) && Finite(seat.FacingZ);}
        private static bool Finite(double value){return !double.IsNaN(value) && !double.IsInfinity(value);}
    }
}
