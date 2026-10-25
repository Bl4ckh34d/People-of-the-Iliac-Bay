using System;
using System.Globalization;
namespace PeopleOfTheIliacBay.Core
{
    public sealed class ConversationLocationSnapshot
    {
        private readonly AppointmentPlace place;
        public readonly double CapturedAt;
        public ConversationLocationSnapshot(double capturedAt,AppointmentPlace location)
        {
            if(double.IsNaN(capturedAt) || double.IsInfinity(capturedAt) || capturedAt<0 || location==null ||
                location.SettlementId<0 || (location.Kind!="world-point" && location.Kind!="building") ||
                location.Kind=="building" && location.BuildingKey<0 || !Valid(location.X) || !Valid(location.Y) || !Valid(location.Z))
                throw new ArgumentException("Invalid captured conversation location.");
            CapturedAt=capturedAt;place=AppointmentPlace.Copy(location);
        }
        private static bool Valid(double value){return !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value)<=1000000;}
        public AppointmentPlace PlaceCopy(){return AppointmentPlace.Copy(place);}
        public string Context()
        {
            return "HERE is the fixed player location captured at submission: "+place.Kind+", settlement="+place.SettlementId+
                ", building="+place.BuildingKey+", stable settlement-local coordinates="+place.X.ToString("R",CultureInfo.InvariantCulture)+","+
                place.Y.ToString("R",CultureInfo.InvariantCulture)+","+place.Z.ToString("R",CultureInfo.InvariantCulture)+
                ". Detail: "+place.Detail+". Select HERE by meaning when this is the intended meeting place. Never output coordinates; later player movement does not move this saved place.";
        }
    }
}
