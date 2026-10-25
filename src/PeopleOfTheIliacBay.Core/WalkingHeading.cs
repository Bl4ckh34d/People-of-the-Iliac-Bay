using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class WalkingHeading
    {
        public static double StepDegrees(double currentDegrees,double targetDegrees,
            double responsePerSecond,double maxTurnDegreesPerSecond,double deltaSeconds,bool smooth)
        {
            if(!Finite(currentDegrees) || !Finite(targetDegrees) || !Finite(responsePerSecond) ||
                responsePerSecond<0 || !Finite(maxTurnDegreesPerSecond) || maxTurnDegreesPerSecond<0 ||
                !Finite(deltaSeconds) || deltaSeconds<0)
                throw new ArgumentException("Walking heading inputs must be finite and non-negative where required.");
            currentDegrees=Normalize(currentDegrees);
            targetDegrees=Normalize(targetDegrees);
            if(!smooth)return targetDegrees;
            double difference=NormalizeSigned(targetDegrees-currentDegrees);
            if(Math.Abs(difference)<1e-12)return targetDegrees;
            if(responsePerSecond==0 || maxTurnDegreesPerSecond==0 || deltaSeconds==0)return currentDegrees;

            double error=Math.Abs(difference);
            double threshold=maxTurnDegreesPerSecond/responsePerSecond;
            double remainingError;
            if(error>threshold && Finite(threshold))
            {
                double cappedSeconds=(error-threshold)/maxTurnDegreesPerSecond;
                if(deltaSeconds<=cappedSeconds)
                    remainingError=error-maxTurnDegreesPerSecond*deltaSeconds;
                else
                    remainingError=threshold*Math.Exp(-responsePerSecond*(deltaSeconds-cappedSeconds));
            }
            else
                remainingError=error*Math.Exp(-responsePerSecond*deltaSeconds);

            double step=Math.Sign(difference)*(error-remainingError);
            if(Math.Abs(difference-step)<1e-12)return targetDegrees;
            return Normalize(currentDegrees+step);
        }

        private static double NormalizeSigned(double degrees)
        {
            degrees=(degrees+540)%360;
            if(degrees<0)degrees+=360;
            return degrees-180;
        }
        private static double Normalize(double degrees)
        {
            degrees%=360;
            return degrees<0?degrees+360:degrees;
        }
        private static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
    }
}
