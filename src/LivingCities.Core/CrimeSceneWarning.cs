using System;
namespace LivingCities.Core
{
    [Serializable]
    public sealed class CrimeSceneWarning
    {
        public bool Issued;
        public double Remaining;
        public bool Violated { get { return Issued && Remaining <= 0; } }
        public bool Begin()
        {
            if (Issued) return false;
            Issued = true; Remaining = 10; return true;
        }
        public void Advance(double realSeconds, bool insideScene, bool paused)
        {
            if (double.IsNaN(realSeconds) || double.IsInfinity(realSeconds) || realSeconds < 0)
                throw new ArgumentException("Invalid warning elapsed time.");
            if (!insideScene) { Issued = false; Remaining = 0; }
            else if (Issued && !paused) Remaining = Math.Max(0, Remaining - realSeconds);
        }
        public static CrimeSceneWarning Copy(CrimeSceneWarning source)
        {
            if (source == null) return null;
            if (double.IsNaN(source.Remaining) || double.IsInfinity(source.Remaining) || source.Remaining < 0 ||
                source.Remaining > 10 || (!source.Issued && source.Remaining != 0))
                throw new ArgumentException("Invalid saved crime scene warning.");
            return new CrimeSceneWarning { Issued = source.Issued, Remaining = source.Remaining };
        }
    }
}
