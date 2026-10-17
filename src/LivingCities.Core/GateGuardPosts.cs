using System;

namespace LivingCities.Core
{
    public sealed class GateGuardPostPlan
    {
        public SavedRoutePoint InsideLeft, InsideRight, InsideReturnLeft, InsideReturnRight, OutsideLeft, OutsideRight;
        public SavedRoutePoint InsideFacing, OutsideFacing;
    }

    public static class GateGuardPosts
    {
        // citywardNormal points from wall toward city. GateAxis runs across opening.
        public static GateGuardPostPlan Build(double centerX, double centerZ,
            double citywardX, double citywardZ, double gateAxisX, double gateAxisZ,
            double halfWallDepth, double halfOpening, double wallClearance, double sideClearance)
        {
            Require(Finite(centerX) && Finite(centerZ) && Finite(citywardX) && Finite(citywardZ) &&
                Finite(gateAxisX) && Finite(gateAxisZ) && Finite(halfWallDepth) && Finite(halfOpening) &&
                Finite(wallClearance) && Finite(sideClearance), "Invalid gate post geometry.");
            Require(halfWallDepth >= 0 && halfOpening > 0 && wallClearance > 0 && sideClearance > 0,
                "Invalid gate post clearance.");
            double normalLength = Math.Sqrt(citywardX * citywardX + citywardZ * citywardZ);
            double axisLength = Math.Sqrt(gateAxisX * gateAxisX + gateAxisZ * gateAxisZ);
            Require(normalLength > 0.0001 && axisLength > 0.0001, "Gate directions must be nonzero.");
            citywardX /= normalLength; citywardZ /= normalLength;
            gateAxisX /= axisLength; gateAxisZ /= axisLength;
            Require(Math.Abs(citywardX * gateAxisX + citywardZ * gateAxisZ) <= 0.01,
                "Gate axis must be perpendicular to cityward direction.");

            double normalOffset = halfWallDepth + wallClearance;
            double returnOffset = normalOffset + 3.0;
            double sideOffset = halfOpening + sideClearance;
            return new GateGuardPostPlan {
                InsideLeft = Point(centerX + citywardX * normalOffset - gateAxisX * sideOffset,
                    centerZ + citywardZ * normalOffset - gateAxisZ * sideOffset),
                InsideRight = Point(centerX + citywardX * normalOffset + gateAxisX * sideOffset,
                    centerZ + citywardZ * normalOffset + gateAxisZ * sideOffset),
                InsideReturnLeft = Point(centerX + citywardX * returnOffset - gateAxisX * sideOffset,
                    centerZ + citywardZ * returnOffset - gateAxisZ * sideOffset),
                InsideReturnRight = Point(centerX + citywardX * returnOffset + gateAxisX * sideOffset,
                    centerZ + citywardZ * returnOffset + gateAxisZ * sideOffset),
                OutsideLeft = Point(centerX - citywardX * normalOffset - gateAxisX * sideOffset,
                    centerZ - citywardZ * normalOffset - gateAxisZ * sideOffset),
                OutsideRight = Point(centerX - citywardX * normalOffset + gateAxisX * sideOffset,
                    centerZ - citywardZ * normalOffset + gateAxisZ * sideOffset),
                InsideFacing = Point(citywardX, citywardZ),
                OutsideFacing = Point(-citywardX, -citywardZ),
            };
        }
        private static SavedRoutePoint Point(double x, double z) { return new SavedRoutePoint { X = x, Z = z }; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
    }
}
