using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class ShelterRouteTests
    {
        private static SavedRoutePoint P(double x,double z=0) { return new SavedRoutePoint{X=x,Z=z}; }
        private static ShelterRoute R(int key,bool open,params SavedRoutePoint[] path)
        {return new ShelterRoute{BuildingKey=key,Accessible=open,Points=new List<SavedRoutePoint>(path)};}
        internal static void Add(List<(string,Action)> cases)
        {
            cases.Add(("Shelter selection prefers a safe open route over a closer door across the attacker",()=>{
                var candidates=new[]{R(1,true,P(0),P(5)),R(2,false,P(0),P(-1)),R(3,true,P(0),P(-8))};
                var picked=ShelterRoutes.Nearest(P(0),P(2),candidates);
                Check.Equal(3,picked.BuildingKey);
                candidates[2].Points[1].X=-99;Check.Near(-8,picked.Points[1].X);
                Check.True(ShelterRoutes.Nearest(P(0),P(2),new[]{candidates[0],candidates[1]})==null);
            }));
            cases.Add(("Shelter selection uses route length with stable ties and rejects disconnected routes",()=>{
                var a=R(4,true,P(0),P(-3),P(-3,3));
                var b=R(2,true,P(0),P(-6));
                var invalid=R(1,true,P(-1),P(-2));
                Check.Equal(2,ShelterRoutes.Nearest(P(0),P(2),new[]{a,b,invalid}).BuildingKey);
                Check.Equal(2,ShelterRoutes.Nearest(P(0),P(2),new[]{invalid,b,a}).BuildingKey);
                Check.True(ShelterRoutes.Nearest(P(0),P(2),new[]{R(1,true,P(0),P(double.NaN))})==null);
            }));
        }
    }
}
