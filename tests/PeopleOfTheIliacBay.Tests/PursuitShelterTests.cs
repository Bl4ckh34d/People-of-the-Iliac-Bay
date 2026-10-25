using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class PursuitShelterTests
    {
        private static SavedRoutePoint P(double x,double z=0){return new SavedRoutePoint{X=x,Z=z};}
        private static ShelterRoute R(int key,params SavedRoutePoint[] points)
        {return new ShelterRoute {BuildingKey=key,Accessible=true,Points=new List<SavedRoutePoint>(points)};}
        internal static void Add(List<(string,Action)> cases)
        {
            cases.Add(("A known closing pursuer behind the NPC triggers a shelter search with cooldown",()=>{
                var pursuit=new ShelterPursuit();
                Check.True(!pursuit.ShouldSearch("foe",0,P(0),P(-20),true));
                Check.True(pursuit.ShouldSearch("foe",1,P(1),P(-10),true));
                Check.True(!pursuit.ShouldSearch("foe",1.1,P(1),P(-9),true));
                Check.True(!pursuit.ShouldSearch("foe",2,P(2),P(-7),true));
                Check.True(pursuit.ShouldSearch("foe",3.1,P(3),P(-2),true));
            }));
            cases.Add(("Stationary distant and unidentified threats cannot pretend to pursue",()=>{
                var pursuit=new ShelterPursuit();
                Check.True(!pursuit.ShouldSearch("foe",0,P(0),P(-12),true));
                Check.True(!pursuit.ShouldSearch("foe",1,P(-1),P(-12),true));
                Check.True(!pursuit.ShouldSearch("foe",2,P(-1),P(-10),false));
                Check.True(!pursuit.ShouldSearch("foe",3,P(-1),P(-9),true));
                Check.True(!new ShelterPursuit().ShouldSearch("foe",1,P(0),P(-1),false));
                Check.True(!new ShelterPursuit().ShouldSearch("foe",1,P(0),P(-30),true));
            }));
            cases.Add(("Slow approach does not trigger an emergency route change outside contact range",()=>{
                var pursuit=new ShelterPursuit();
                Check.True(!pursuit.ShouldSearch("foe",0,P(0),P(-10),true));
                Check.True(!pursuit.ShouldSearch("foe",1,P(0),P(-9.8),true));
                Check.True(!pursuit.ShouldSearch("foe",2,P(0),P(-9.6),true));
                Check.True(pursuit.ShouldSearch("foe",3,P(0),P(-8),true));
            }));
            cases.Add(("An immediately close known threat permits escape without a prior sample",()=>{
                var pursuit=new ShelterPursuit();
                Check.True(pursuit.ShouldSearch("player",10,P(0),P(-4),true));
                Check.True(!pursuit.ShouldSearch("player",10.1,P(0),P(-3),true));
                Check.True(!pursuit.ShouldSearch(null,12,P(0),P(-2),true));
                Check.True(!pursuit.ShouldSearch("foe",double.NaN,P(0),P(-2),true));
            }));
            cases.Add(("Changing threat identity does not reuse a previous pursuit sample",()=>{
                var pursuit=new ShelterPursuit();
                Check.True(!pursuit.ShouldSearch("first",0,P(0),P(-20),true));
                Check.True(!pursuit.ShouldSearch("second",1,P(0),P(-10),true));
                Check.True(pursuit.ShouldSearch("second",2,P(0),P(-8),true));
            }));
            cases.Add(("Emergency shelter selects a shorter safe door without mutating the old path",()=>{
                var current=R(1,P(0),P(20));var near=R(2,P(0),P(4));
                var chosen=ShelterPursuit.Choose(P(0),P(-4),current,new[]{near});
                Check.Equal(2,chosen.BuildingKey);Check.Near(0,chosen.Points[0].X);
                near.Points[1].X=999;Check.Near(4,chosen.Points[1].X);
                Check.Near(20,current.Points[1].X);
            }));
            cases.Add(("Emergency shelter rejects a door across the pursuer and avoids marginal switching",()=>{
                var current=R(1,P(0),P(5));var across=R(2,P(0),P(-3));
                var marginal=R(3,P(0),P(4.5));
                Check.Equal(1,ShelterPursuit.Choose(P(0),P(-2),current,new[]{across,marginal}).BuildingKey);
                Check.True(ShelterPursuit.Choose(P(0),P(-2),null,new[]{across})==null);
                var closed=R(4,P(0),P(1));closed.Accessible=false;
                Check.Equal(1,ShelterPursuit.Choose(P(0),P(-2),current,new[]{closed}).BuildingKey);
            }));
            cases.Add(("A safer door replaces an obsolete unsafe shelter route",()=>{
                var unsafeRoute=R(1,P(0),P(10));var safe=R(2,P(0),P(-7));
                Check.Equal(2,ShelterPursuit.Choose(P(0),P(3),unsafeRoute,new[]{safe}).BuildingKey);
            }));
            cases.Add(("Emergency doors must lie ahead even when a rearward detour is collision-safe",()=>{
                var current=R(1,P(0),P(40));var rear=R(2,P(0),P(0,14),P(-8,14));
                var side=R(3,P(0),P(0,3));var ahead=R(4,P(0),P(4),P(4,2));
                Check.Equal(2,ShelterRoutes.Nearest(P(0),P(-2),new[]{rear}).BuildingKey);
                Check.Equal(1,ShelterPursuit.Choose(P(0),P(-2),current,new[]{rear,side}).BuildingKey);
                Check.Equal(4,ShelterPursuit.Choose(P(0),P(-2),current,new[]{rear,side,ahead}).BuildingKey);
                Check.True(ShelterPursuit.Choose(P(0),P(-2),null,new[]{rear,side})==null);
            }));
            cases.Add(("Escape heading permits a nearby door after a corner but never toward the pursuer",()=>{
                var alongStreet=R(1,P(0),P(0,4));var toward=R(2,P(0),P(0,4),P(-3,4));
                Check.Equal(1,ShelterPursuit.Choose(P(0),P(-2),null,new[]{alongStreet,toward},P(0,1)).BuildingKey);
                Check.Equal(1,ShelterPursuit.Choose(P(0),P(-2),null,new[]{R(1,P(0),P(4))},P(-1)).BuildingKey);
            }));
            cases.Add(("Replanned shelter locks the selected door only after actual refuge arrival and survives reload",()=>{
                var store=new WorldStateStore(3);var town=store.GetOrCreate(1,new[]{7,8},1);
                var victim=town.Households[0].Residents[0];var runner=town.Households[1].Residents[0];
                var alarm=store.RecordViolence(1,victim.Id,100);store.LocateViolence(alarm.Id,-1,0,0,0);
                var person=store.Witness(alarm.Id,runner.Id,101);person.Reaction=CivilianReaction.Flee;
                person.HasPosition=true;person.X=0;person.Z=0;
                var chosen=ShelterPursuit.Choose(P(0),P(-4),R(7,P(0),P(40)),new[]{R(8,P(0),P(5))});
                Check.Equal(8,chosen.BuildingKey);Check.Near(0,person.X);
                Check.True(!store.ShelterLocked(1,8,110));
                Check.Throws<ArgumentException>(()=>store.RecordShelterArrival(alarm.Id,runner.Id,110));
                person.X=5;person.ReachedRefuge=true;person.BuildingKey=chosen.BuildingKey;
                store.RecordShelterArrival(alarm.Id,runner.Id,120);
                Check.True(store.ShelterLocked(1,8,121));Check.True(!store.ShelterLocked(1,7,121));
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.True(restored.ShelterLocked(1,8,3719));Check.True(!restored.ShelterLocked(1,8,3720));
            }));
            cases.Add(("A witness running to a guard can divert to shelter without reporting at the door",()=>{
                var store=new WorldStateStore(3);var town=store.GetOrCreate(1,new[]{7,8,9},1);
                var victim=town.Households[0].Residents[0];var runner=town.Households[1].Residents[0];
                var alarm=store.RecordViolence(1,victim.Id,100);store.LocateViolence(alarm.Id,7,0,0,0);
                var person=store.Witness(alarm.Id,runner.Id,100);person.Reaction=CivilianReaction.Flee;person.HasPosition=true;person.BuildingKey=7;
                person.Escape=CivilianEscape.Create(person,100,new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=1}},
                    new List<SavedRoutePoint>{P(0),P(20)},1,1);
                store.AdvanceEscapes(1,104);Check.Near(3,person.X);
                person.Escape=person.Escape.RedirectToShelter(104,new List<SavedRoutePoint>{P(3),P(8)},9);
                Check.True(!store.ShelterLocked(1,9,104));
                int guardReplans=0;
                Check.Equal(0,store.AdvanceEscapes(1,108.9,true,(a,p,t)=>guardReplans++).Count);
                Check.Equal(-1,person.BuildingKey);Check.True(!person.ReachedRefuge);Check.Near(7.9,person.X);
                Check.Equal(0,store.AdvanceEscapes(1,109,true,(a,p,t)=>guardReplans++).Count);
                Check.Equal(0,guardReplans);Check.Equal(9,person.BuildingKey);
                Check.True(person.DirectWitness&&person.ReachedRefuge&&person.RefugeSealed);
                Check.True(store.ShelterLocked(1,9,110));Check.True(!store.ShelterLocked(1,7,110));
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.Equal(9,restored.Snapshot().AlarmIncidents[0].People[0].Escape.ShelterBuildingKey);
                Check.Equal(0,restored.AdvanceEscapes(1,111,true).Count);
                Check.True(restored.ShelterLocked(1,9,3708));Check.True(!restored.ShelterLocked(1,9,3709));
            }));
            cases.Add(("Historical native escape saves retain their guard destination when shelter field is absent",()=>{
                var person=new AlarmPerson {HasPosition=true,BuildingKey=7,Reaction=CivilianReaction.Flee};
                var escape=CivilianEscape.Create(person,100,new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=1}},
                    new List<SavedRoutePoint>{P(0),P(20)},1,1);
                var serializer=new FullSerializer.fsSerializer();FullSerializer.fsData data;
                Check.True(serializer.TrySerialize(escape,out data).Succeeded);
                Check.True(data.AsDictionary.Remove("ShelterBuildingKey"));
                CivilianEscape restored=null;Check.True(serializer.TryDeserialize(data,ref restored).Succeeded);
                restored=CivilianEscape.Copy(restored);Check.Equal(-1,restored.ShelterBuildingKey);
                var snapshot=new AlarmPerson();restored.Advance(snapshot,121);
                Check.Equal(-1,snapshot.BuildingKey);Check.True(!snapshot.ReachedRefuge);
            }));
        }
    }
}
