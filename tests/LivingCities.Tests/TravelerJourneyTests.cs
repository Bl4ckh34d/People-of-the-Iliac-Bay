using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class TravelerJourneyTests
    {
        private static TravelerJourney Make(bool walls,double arrival,int nights=1,double commute=600,uint seed=42)
        { return TravelerJourney.Create(seed,arrival,nights,commute,walls,new[] { new RoutePoint(0,0),new RoutePoint(30,0),new RoutePoint(30,40) }); }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Walled travelers wait for dawn but unwalled arrivals can enter at night",()=>{
                var walled=Make(true,20*3600); var village=Make(false,20*3600);
                Check.Equal(30*3600d,walled.EntryAt); Check.Equal(20*3600d,village.EntryAt);
                Check.Equal(TravelerPhase.NotArrived,walled.Evaluate(23*3600).Phase);
                Check.Equal(TravelerPhase.Arriving,walled.Evaluate(walled.EntryAt).Phase);
                Check.Equal(6*3600d,Make(true,6*3600).EntryAt);
                Check.Equal(30*3600d,Make(true,18*3600).EntryAt);
            }));
            cases.Add(("Traveler route uses distance and reverses continuously after checkout",()=>{
                var journey=Make(false,12*3600,1,700);
                var halfway=journey.Evaluate(journey.EntryAt+350);
                Check.Equal(30d,halfway.Position.X); Check.Equal(5d,halfway.Position.Z);
                Check.Equal(TravelerPhase.AtInn,journey.Evaluate(journey.CheckInAt).Phase);
                Check.True(journey.Evaluate(journey.CheckInAt).WithinStay);
                Check.True(!journey.Evaluate(journey.CheckOutAt).WithinStay);
                var outbound=journey.Evaluate(journey.LeaveInnAt+350);
                Check.Equal(TravelerPhase.Leaving,outbound.Phase);
                Check.Equal(30d,outbound.Position.X); Check.Equal(5d,outbound.Position.Z);
                Check.Equal(TravelerPhase.Departed,journey.Evaluate(journey.ExitAt).Phase);
            }));
            cases.Add(("Paid nights and sleep survive direct time jumps and reconstruction",()=>{
                var one=Make(false,12*3600); var three=Make(false,12*3600,3);
                Check.Equal(2*86400d,three.CheckOutAt-one.CheckOutAt);
                Check.Equal(TravelerPhase.Sleeping,three.Evaluate(25*3600).Phase);
                var rebuilt=Make(false,12*3600,3);
                Check.Equal(three.CheckOutAt,rebuilt.CheckOutAt);
                Check.Equal(three.Evaluate(49*3600).Phase,rebuilt.Evaluate(49*3600).Phase);
                Check.Equal(TravelerPhase.Departed,three.Evaluate(100*86400).Phase);
                var early=Make(false,2*3600);
                Check.True(early.CheckOutAt<86400);
            }));
            cases.Add(("Closed outbound gates delay departure without extending paid bed",()=>{
                var journey=Make(true,6*3600,1,10*3600);
                Check.True(journey.LeaveInnAt>journey.CheckOutAt);
                Check.Equal(TravelerPhase.WaitingForGate,journey.Evaluate(journey.CheckOutAt).Phase);
                Check.True(!journey.Evaluate(journey.CheckOutAt+1).WithinStay);
                Check.Equal(6*3600d,journey.ExitAt%86400);
            }));
            cases.Add(("Traveler itinerary rejects invalid intervals and owns its route",()=>{
                Check.Throws<ArgumentException>(()=>Make(false,0,0));
                Check.Throws<ArgumentException>(()=>Make(false,-1));
                Check.Throws<ArgumentException>(()=>Make(false,0,1,0));
                Check.Throws<ArgumentException>(()=>TravelerJourney.Create(1,0,1,60,false,new[] {new RoutePoint(0,0),new RoutePoint(0,0)}));
                var route=new[] {new RoutePoint(0,0),new RoutePoint(10,0)};
                var journey=TravelerJourney.Create(1,0,1,60,false,route); route[1]=new RoutePoint(100,0);
                Check.Equal(5d,journey.Evaluate(30).Position.X);
                Check.Throws<ArgumentOutOfRangeException>(()=>journey.Evaluate(double.NaN));
            }));
        }
    }
}
