using System;
namespace LivingCities.Core
{
    // Route nodes refer to map-pixel centres; terrain UV origin is southwest.
    public struct RoadTerrainPoint
    {
        public readonly int PixelX,PixelY;
        public readonly double U,V;
        public RoadTerrainPoint(RoutePoint position)
        {
            if(double.IsNaN(position.X) || double.IsNaN(position.Z) || position.X<0 || position.X>=999.5 ||
                position.Z<0 || position.Z>=499.5) throw new ArgumentException("Invalid road terrain position.");
            PixelX=(int)Math.Floor(position.X+0.5); PixelY=(int)Math.Floor(position.Z+0.5);
            U=position.X+0.5-PixelX; V=1-(position.Z+0.5-PixelY);
        }
    }
}
