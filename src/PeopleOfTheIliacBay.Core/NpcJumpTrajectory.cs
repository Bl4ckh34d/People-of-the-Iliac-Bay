using System;

namespace PeopleOfTheIliacBay.Core
{
    // Ballistic arithmetic only. The native adapter must prove floor support,
    // body clearance and the actor's current movement capability separately.
    public sealed class NpcJumpTrajectory
    {
        public double Duration { get; private set; }
        public double HorizontalSpeed { get; private set; }
        public double LaunchSpeed { get; private set; }
        public double Gravity { get; private set; }

        public static bool TryCreate(double distance, double height, double launchSpeed,
            double gravity, double maximumHorizontalSpeed, out NpcJumpTrajectory trajectory)
        {
            trajectory = null;
            if (!Finite(distance) || !Finite(height) || !Finite(launchSpeed) ||
                !Finite(gravity) || !Finite(maximumHorizontalSpeed) || distance <= 0 ||
                launchSpeed <= 0 || gravity <= 0 || maximumHorizontalSpeed <= 0) return false;
            double discriminant = launchSpeed * launchSpeed - 2 * gravity * height;
            if (!Finite(discriminant) || discriminant < 0) return false;
            // The positive root reaches the floor while descending. Choosing the
            // ascending crossing could drive the body into a raised ledge.
            double duration = (launchSpeed + Math.Sqrt(discriminant)) / gravity;
            double horizontalSpeed = distance / duration;
            if (!Finite(duration) || duration <= 0 || !Finite(horizontalSpeed) ||
                horizontalSpeed > maximumHorizontalSpeed) return false;
            trajectory = new NpcJumpTrajectory {
                Duration = duration, HorizontalSpeed = horizontalSpeed,
                LaunchSpeed = launchSpeed, Gravity = gravity
            };
            return true;
        }

        public double HeightAt(double seconds)
        {
            if (!Finite(seconds) || seconds < 0 || seconds > Duration)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            return LaunchSpeed * seconds - Gravity * seconds * seconds / 2;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
