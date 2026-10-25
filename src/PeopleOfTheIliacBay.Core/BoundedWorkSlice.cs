using System;
using System.Collections;

namespace PeopleOfTheIliacBay.Core
{
    public static class BoundedWorkSlice
    {
        public static bool Advance(IEnumerator work,Func<double> elapsedMilliseconds,Func<bool> canAdvance,int maximumSteps=512,double maximumMilliseconds=4)
        {
            if(work==null || elapsedMilliseconds==null || canAdvance==null) throw new ArgumentNullException();
            if(maximumSteps<1 || double.IsNaN(maximumMilliseconds) || double.IsInfinity(maximumMilliseconds) || maximumMilliseconds<=0)
                throw new ArgumentOutOfRangeException();
            for(int step=0;step<maximumSteps;step++)
            {
                if(!canAdvance() || step>0 && elapsedMilliseconds()>=maximumMilliseconds) return false;
                if(!work.MoveNext()) return true;
            }
            return false;
        }
    }
}
