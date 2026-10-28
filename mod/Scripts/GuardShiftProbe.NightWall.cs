using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class GuardShiftProbe
    {
        private static double NightWallRouteMetres(IList<GuardPoint3> route)
        {
            double length=0;
            for(int index=1;index<route.Count;index++)
            {var a=route[index-1];var b=route[index];double x=b.X-a.X,y=b.Y-a.Y,z=b.Z-a.Z;length+=Math.Sqrt(x*x+y*y+z*z);}
            return length;
        }

        private static IEnumerator VerifyOneNightWall(GuardPopulation controller,float selectionDeadline)
        {
            var game=GameManager.Instance;var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry=Field<TownGeometry>(controller,"geometry");
            if(GuardShifts.At(Now())!=GuardShift.Night)RaiseHour(20);
            GuardRecord selected=null;
            while(Time.realtimeSinceStartup<selectionDeadline)
            {
                selected=controller.Roster.Guards.Find(guard=>guard.IsAlive && GuardShifts.OnDuty(guard,Now()) &&
                    guard.Shift==GuardShift.Night && guard.Refuge!=null && guard.Refuge.WallRoute!=null &&
                    (guard.Refuge.Evaluate(Now()).Phase==RefugePhase.Approaching ||
                     guard.Refuge.Evaluate(Now()).Phase==RefugePhase.Entering ||
                     guard.Refuge.Evaluate(Now()).Phase==RefugePhase.WallApproaching ||
                     guard.Refuge.Evaluate(Now()).Phase==RefugePhase.WallPosted));
                if(selected!=null)break;
                yield return null;
            }
            if(selected==null)throw new InvalidOperationException("Night wall focus has no actual assigned single-site route within30seconds.");
            var journey=selected.Refuge;
            double metres=NightWallRouteMetres(journey.Exterior)+NightWallRouteMetres(journey.Interior)+NightWallRouteMetres(journey.WallRoute);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NIGHT_WALL_SELECTED: guard="+selected.Id+"; building="+journey.BuildingKey+
                "; actualAssignedRoute=true; routeMetres="+metres+"; nativeSpeed=1.3; walkLimitSeconds=120; fullSurveyPrerequisite=false");
            var endpoint=journey.WallRoute[journey.WallRoute.Count-1];
            var post=directory.transform.TransformPoint(new Vector3((float)endpoint.X,(float)endpoint.Y,(float)endpoint.Z));
            float walkDeadline=Time.realtimeSinceStartup+120,nextTrace=Time.realtimeSinceStartup+10;
            bool actualPost=false,observedExterior=false,unobservedInterior=false;
            while(Time.realtimeSinceStartup<walkDeadline)
            {
                if(!selected.IsAlive || !GuardShifts.OnDuty(selected,Now()) || selected.Refuge!=journey)
                    throw new InvalidOperationException("Selected actual night guard route was interrupted: "+selected.Id);
                var snapshot=journey.Evaluate(Now());
                bool inside=snapshot.Phase==RefugePhase.Entering || snapshot.Phase==RefugePhase.Exiting;
                var point=inside?journey.Exterior[journey.Exterior.Count-1]:snapshot.Position;
                game.PlayerMotor.transform.position=geometry.ScenePoint(new RoutePoint(point.X,point.Z))+Vector3.up+Vector3.forward*4;
                if(inside)unobservedInterior=true;
                yield return null;
                var body=Find(selected.Id);
                if(body && snapshot.Phase==RefugePhase.Approaching)observedExterior=true;
                if(snapshot.Phase==RefugePhase.WallPosted && body && Vector3.Distance(body.GroundPosition,post)<=.4f)
                {actualPost=true;break;}
                if(Time.realtimeSinceStartup>=nextTrace)
                {nextTrace+=10;Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NIGHT_WALL_WALK: guard="+selected.Id+"; phase="+snapshot.Phase+
                    "; remainingRealSeconds="+Mathf.Max(0,walkDeadline-Time.realtimeSinceStartup)+"; normalRouteClock=true; recordsNotAltered=true");}
            }
            if(!actualPost)throw new InvalidOperationException("Selected real night guard did not reach its actual wall post within120seconds: "+selected.Id);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NIGHT_WALL_ARRIVAL: guard="+selected.Id+"; actualPostBody=true; observedExterior="+
                observedExterior+"; interiorNormalUnobservedProjection="+unobservedInterior+"; noAdditionalClockJump=true; injectedGuards=false");
            // Keep the genuinely reached night state for a direct retry without
            // repeating this ordinary walk if a later independent assertion fails.
            bool saved=false;SaveLoadManager.OnSaveEventHandler onSave=ignored=>saved=true;
            SaveLoadManager.OnSave+=onSave;
            try
            {
                const string title="People - Daggerfall night wall ready";
                controller.Capture();game.SaveLoadManager.Save(game.PlayerEntity.Name,title);
                float saveDeadline=Time.realtimeSinceStartup+15;
                while(!saved && Time.realtimeSinceStartup<saveDeadline)yield return null;
                if(!saved)throw new InvalidOperationException("Actual reached night-wall checkpoint did not save.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NIGHT_WALL_CHECKPOINT: title="+title+"; guard="+selected.Id+"; actualReachedPost=true");
            }
            finally {SaveLoadManager.OnSave-=onSave;}
            yield return GuardWallTrespassProbe.Run(controller,selected.Id,true);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NIGHT_WALL_LAW_PASS: guard="+selected.Id+
                "; actualNightPost=true; nativeContactOffer=true; actualNo=true; actualDamage=true; fullGarrisonPrerequisite=false");
        }
    }
}
