using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class ResidentInnJourneyTests
    {
        private static List<GuardPoint3> Path(params double[] xy)
        {
            var result=new List<GuardPoint3>();
            for(int i=0;i<xy.Length;i+=2) result.Add(new GuardPoint3 {X=xy[i],Y=xy[i+1],Z=0});
            return result;
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Inn reception follows stairs, waits at keeper, reaches room and exits on time",()=>{
                var journey=ResidentInnJourney.Create(Path(0,0,3,4),Path(3,4,9,4),Path(9,4,3,4,0,0),100,200,2,20);
                Check.Near(110,journey.CounterAt); Check.Near(130,journey.ServiceEndsAt);
                Check.Near(142,journey.RoomAt); Check.Near(178,journey.ExitAt);
                Check.Equal(ResidentInnPhase.Before,journey.Evaluate(99).Phase);
                var stair=journey.Evaluate(106); Check.Near(1.8,stair.Position.X); Check.Near(2.4,stair.Position.Y);
                Check.Equal(ResidentInnPhase.AtKeeper,journey.Evaluate(110).Phase);
                Check.Equal(ResidentInnPhase.ToRoom,journey.Evaluate(130).Phase);
                Check.Near(6,journey.Evaluate(136).Position.X);
                Check.Equal(ResidentInnPhase.InRoom,journey.Evaluate(142).Phase);
                Check.Equal(ResidentInnPhase.Leaving,journey.Evaluate(178).Phase);
                Check.Near(6,journey.Evaluate(184).Position.X);
                Check.Equal(ResidentInnPhase.Finished,journey.Evaluate(200).Phase);
                Check.Near(0,journey.Evaluate(201).Position.X);
            }));
            cases.Add(("Inn reception copies geometry and reconstructs without restarting service",()=>{
                var entry=Path(0,0,3,4); var bedroom=Path(3,4,9,4); var exit=Path(9,4,3,4,0,0);
                var a=ResidentInnJourney.Create(entry,bedroom,exit,100,200,2,20);
                var b=ResidentInnJourney.Create(entry,bedroom,exit,100,200,2,20);
                entry[1].X=999; bedroom.Clear(); exit[0].Y=999;
                foreach(double now in new[]{100d,106,120,135,155,190,200})
                { Check.Equal(a.Evaluate(now).Phase,b.Evaluate(now).Phase); Check.Near(a.Evaluate(now).Position.X,b.Evaluate(now).Position.X); }
                var sample=a.Evaluate(120); sample.Position.X=999; Check.Near(3,a.Evaluate(120).Position.X);
            }));
            cases.Add(("Inn reception rejects disconnected routes, insufficient stay and invalid timing",()=>{
                Check.Throws<ArgumentException>(()=>ResidentInnJourney.Create(Path(0,0,3,4),Path(4,4,9,4),Path(9,4,0,0),100,200,2,20));
                Check.Throws<ArgumentException>(()=>ResidentInnJourney.Create(Path(0,0,3,4),Path(3,4,9,4),Path(9,4,1,0),100,200,2,20));
                Check.Throws<ArgumentException>(()=>ResidentInnJourney.Create(Path(0,0,3,4),Path(3,4,9,4),Path(9,4,0,0),100,130,2,20));
                Check.Throws<ArgumentException>(()=>ResidentInnJourney.Create(Path(0,0,3,4),Path(3,4,9,4),Path(9,4,0,0),100,200,2,double.NaN));
            }));
        }
    }
}
