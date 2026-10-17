using System;
using System.Collections.Generic;
using LivingCities.Core;
namespace LivingCities.Tests
{
    internal static class OvernightSerializationTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Native timestamp rounding does not invalidate an overnight room hold",()=>{
                var route=new List<SavedRoutePoint>{new SavedRoutePoint {X=0,Z=0},new SavedRoutePoint {X=2,Z=0}};
                var stop=new ResidentVisitStop {BuildingKey=4,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=12.12345,DwellSeconds=86400.12345,Route=route};
                var destination=new ResidentDestination {Stops=new List<ResidentVisitStop>{stop},ExitWalkSeconds=12.12345,
                    ExitRoute=new List<SavedRoutePoint>{new SavedRoutePoint {X=2,Z=0},new SavedRoutePoint {X=0,Z=0}}};
                double arrival=55000000000.12345;
                var visits=new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(arrival,destination).ScheduledVisits());
                stop.KeepRoomUntil=visits[0].LeaveAt-.0002;
                Check.True(ResidentVisitJourney.Create(arrival,destination)!=null);
                double itineraryEnd=ResidentVisitJourney.Create(arrival,destination).EndsAt;
                stop.KeepRoomUntil=itineraryEnd+.0002;
                Check.True(ResidentVisitJourney.Create(arrival,destination)!=null);
                stop.KeepRoomUntil=itineraryEnd+2;
                Check.Throws<ArgumentException>(()=>ResidentVisitJourney.Create(arrival,destination));
                stop.KeepRoomUntil=visits[0].LeaveAt-2;
                Check.Throws<ArgumentException>(()=>ResidentVisitJourney.Create(arrival,destination));
            }));
        }
    }
}
